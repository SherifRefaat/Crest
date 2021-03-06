﻿// Copyright (c) Samuel Cragg.
//
// Licensed under the MIT license. See LICENSE file in the project root for
// full license information.

namespace Crest.Host.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Runtime.Serialization;
    using Crest.Host.Serialization.Internal;

    /// <summary>
    /// Generates a class that can serialize a specific type at runtime.
    /// </summary>
    internal sealed partial class ClassSerializerGenerator : TypeSerializerGenerator
    {
        private readonly Func<Type, Type> generateSerializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClassSerializerGenerator"/> class.
        /// </summary>
        /// <param name="generateSerializer">
        /// Used to get additional serializers for nested classes.
        /// </param>
        /// <param name="module">The dynamic module to output the types to.</param>
        /// <param name="baseClass">
        /// The type for the generated classes to inherit from.
        /// </param>
        public ClassSerializerGenerator(Func<Type, Type> generateSerializer, ModuleBuilder module, Type baseClass)
            : base(module, baseClass)
        {
            this.generateSerializer = generateSerializer;

            Type classSerializerInterface = GetGenericInterfaceImplementation(
                baseClass.GetTypeInfo(),
                typeof(IClassSerializer<>));
        }

        /// <summary>
        /// Generates a serializer for the specific type.
        /// </summary>
        /// <param name="classType">The class to serialize.</param>
        /// <returns>A type implementing <see cref="ITypeSerializer"/>.</returns>
        public Type GenerateFor(Type classType)
        {
            IReadOnlyList<PropertyInfo> properties = GetProperties(classType);
            TypeSerializerBuilder builder = this.CreateType(classType, classType.Name);

            IReadOnlyDictionary<string, FieldInfo> metadata =
                this.CreateMetadataFields(builder, properties);

            // Create the methods first, so that if it needs any nested
            // serializers we can initialize them in the constructors
            IEnumerable<FieldBuilder> fields =
                this.EmitMethods(builder, properties, metadata);

            // Create the constructors
            this.EmitConstructor(builder, fields, this.BaseClass);
            this.EmitConstructor(builder, fields, typeof(Stream), typeof(SerializationMode));

            // Build the type and set the static metadata
            Type generatedType = builder.GenerateType();
            this.SetMetadataFields(generatedType.GetTypeInfo(), properties, metadata);
            return generatedType;
        }

        private static IReadOnlyList<PropertyInfo> GetProperties(Type type)
        {
            int DataOrder(PropertyInfo property)
            {
                DataMemberAttribute dataMember = property.GetCustomAttribute<DataMemberAttribute>();
                return (dataMember != null) ? dataMember.Order : int.MaxValue;
            }

            bool IncludeProperty(PropertyInfo property)
            {
                BrowsableAttribute browsable = property.GetCustomAttribute<BrowsableAttribute>();
                if ((browsable != null) && !browsable.Browsable)
                {
                    return false;
                }

                return property.CanRead && property.CanWrite;
            }

            return type.GetProperties()
                       .Where(IncludeProperty)
                       .OrderBy(DataOrder)
                       .ToList();
        }

        private IReadOnlyDictionary<string, FieldInfo> CreateMetadataFields(
            TypeSerializerBuilder builder,
            IReadOnlyList<PropertyInfo> properties)
        {
            var fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            foreach (PropertyInfo property in properties)
            {
                FieldBuilder field = builder.CreateMetadataField(property.Name);
                fields.Add(property.Name, field);
            }

            return fields;
        }

        private void EmitConstructor(
            TypeSerializerBuilder builder,
            IEnumerable<FieldBuilder> nestedSerializers,
            params Type[] parameters)
        {
            this.EmitConstructor(
                builder,
                generator =>
                {
                    foreach (FieldBuilder serializerField in nestedSerializers)
                    {
                        this.EmitInitializeField(generator, serializerField);
                    }
                },
                parameters);
        }

        private void EmitInitializeField(ILGenerator generator, FieldBuilder serializerField)
        {
            // This method generates code to do this:
            //     this.field = new Serializer(this)
            ConstructorInfo serializerConstructor =
                serializerField.FieldType.GetConstructor(new[] { this.BaseClass });

            // this.field = ...
            generator.EmitLoadArgument(0);

            // field = new Serializer(this)
            generator.EmitLoadArgument(0);
            generator.Emit(OpCodes.Newobj, serializerConstructor);

            // this.field = ...
            generator.Emit(OpCodes.Stfld, serializerField);
        }

        private IEnumerable<FieldBuilder> EmitMethods(
            TypeSerializerBuilder builder,
            IReadOnlyList<PropertyInfo> properties,
            IReadOnlyDictionary<string, FieldInfo> metadata)
        {
            var writeMethod = new WriteMethodEmitter(this, builder, metadata);
            writeMethod.WriteProperties(properties);
            this.EmitWriteArray(builder, writeMethod.GeneratedMethod);

            var readMethod = new ReadMethodEmitter(this, builder, writeMethod.NestedSerializerFields);
            readMethod.EmitReadMethod(GetProperties(builder.SerializedType));
            this.EmitReadArrayMethod(builder);

            return writeMethod.NestedSerializerFields.Values;
        }

        private void EmitReadArrayMethod(TypeSerializerBuilder builder)
        {
            MethodBuilder methodBuilder = builder.CreatePublicVirtualMethod(
                nameof(ITypeSerializer.ReadArray));

            methodBuilder.SetReturnType(typeof(Array));
            ILGenerator generator = methodBuilder.GetILGenerator();
            var arrayEmitter = new ArrayDeserializeEmitter(generator, this.BaseClass, this.Methods)
            {
                CreateLocal = generator.DeclareLocal,
                ReadValue = (g, _) =>
                {
                    // The Read method returns an object, so cast it to the
                    // correct type
                    // (T)this.Read()
                    generator.EmitLoadArgument(0);
                    generator.EmitCall(
                        OpCodes.Callvirt,
                        this.Methods.TypeSerializer.Read,
                        null);
                    generator.Emit(OpCodes.Castclass, builder.SerializedType);
                }
            };

            arrayEmitter.EmitReadArray(builder.SerializedType.MakeArrayType());
            generator.Emit(OpCodes.Ret);
        }

        private void EmitWriteArray(TypeSerializerBuilder builder, MethodInfo generatedMethod)
        {
            MethodBuilder methodBuilder = builder.CreatePublicVirtualMethod(
                    nameof(ITypeSerializer.WriteArray));

            methodBuilder.SetParameters(typeof(Array));
            ILGenerator generator = methodBuilder.GetILGenerator();

            var arrayEmitter = new ArraySerializeEmitter(generator, this.BaseClass, this.Methods)
            {
                WriteValue = (_, loadElement) =>
                {
                    // Note the null checking has been done by the array emitter
                    // this.Write(array[i])
                    generator.EmitLoadArgument(0);
                    loadElement(generator);
                    generator.EmitCall(
                        OpCodes.Call,
                        typeof(ITypeSerializer).GetMethod(nameof(ITypeSerializer.Write)),
                        null);
                }
            };

            generator.EmitLoadArgument(1); // 0 = this, 1 = array
            arrayEmitter.EmitWriteArray(builder.SerializedType.MakeArrayType());
            generator.Emit(OpCodes.Ret);
        }

        private void SetMetadataFields(
            TypeInfo type,
            IReadOnlyList<PropertyInfo> properties,
            IReadOnlyDictionary<string, FieldInfo> metadataFields)
        {
            foreach (PropertyInfo property in properties)
            {
                // We need to get the real field instead of the builder so that
                // it can be set
                FieldInfo field = type.GetField(
                    metadataFields[property.Name].Name,
                    BindingFlags.Public | BindingFlags.Static);

                object value = this.Methods.BaseClass.GetMetadata.Invoke(null, new[] { property });
                field.SetValue(null, value);
            }
        }
    }
}
