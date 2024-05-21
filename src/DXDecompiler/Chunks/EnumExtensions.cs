using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace DXDecompiler.Chunks
{
	public static class EnumExtensions
	{
		private static readonly Dictionary<Type, Dictionary<Type, Dictionary<Enum, Attribute[]>>> AttributeValues;

		static EnumExtensions()
		{
			AttributeValues = new Dictionary<Type, Dictionary<Type, Dictionary<Enum, Attribute[]>>>();
		}

		public static string GetDescription<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TEnum>(this TEnum value, ChunkType chunkType = ChunkType.Unknown)
			where TEnum : struct, Enum
		{
			return value.GetAttributeValue<TEnum, DescriptionAttribute, string>((a, v) =>
			{
				var attribute = a.FirstOrDefault(x => x.ChunkType == chunkType);
				if(attribute == null)
					return v.ToString();
				return attribute.Description;
			});
		}

		public static TValue GetAttributeValue<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TEnum, TAttribute, TValue>(this Enum value,
			Func<TAttribute[], Enum, TValue> getValueCallback)
			where TEnum : struct, Enum
			where TAttribute : Attribute
		{
			Type type = value.GetType();

			if(!AttributeValues.TryGetValue(type, out var attributeValuesForType))
			{
				attributeValuesForType = new Dictionary<Type, Dictionary<Enum, Attribute[]>>();
				AttributeValues[type] = attributeValuesForType;
			}

			var attributeType = typeof(TAttribute);
			if(!attributeValuesForType.ContainsKey(attributeType))
				attributeValuesForType[attributeType] = EnumPolyfill.GetValues<TEnum>().Distinct()
					.ToDictionary(x => (Enum)x, GetAttribute<TEnum, TAttribute>);

			var attributeValues = attributeValuesForType[attributeType];
			if(!attributeValues.TryGetValue(value, out Attribute[] attributeValue))
				throw new ArgumentException(string.Format("Could not find attribute value for type '{0}' and value '{1}'.", type, value));
			return getValueCallback((TAttribute[])attributeValue, value);
		}

		private static Attribute[] GetAttribute<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TEnum, TAttribute>(TEnum value)
			where TEnum : struct, Enum
			where TAttribute : Attribute
		{
			return GetAttribute(typeof(TEnum), value.ToString(), typeof(TAttribute));
		}

		private static Attribute[] GetAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type enumType, string field, Type attributeType)
		{
			return Attribute.GetCustomAttributes(enumType.GetField(field), attributeType);
		}
	}
}