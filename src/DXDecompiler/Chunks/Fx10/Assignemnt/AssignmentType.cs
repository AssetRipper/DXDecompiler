using System;
using System.Diagnostics.CodeAnalysis;

namespace DXDecompiler.Chunks.Fx10.Assignemnt
{
	[AttributeUsage(AttributeTargets.Field)]
	public class AssignmentTypeAttribute : Attribute
	{
		//[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
		public Type Type { get; }

		public AssignmentTypeAttribute(Type type)
		{
			Type = type;
		}
	}
}
