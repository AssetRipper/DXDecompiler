﻿using DXDecompiler.DX9Shader.Bytecode.Declaration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DXDecompiler.DX9Shader
{
	public sealed class RegisterState
	{
		public readonly bool ColumnMajorOrder = true;

		private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

		private ICollection<Constant> _constantDefinitions { get; } = new List<Constant>();
		private ICollection<ConstantInt> _constantIntDefinitions { get; } = new List<ConstantInt>();
		public IDictionary<RegisterKey, RegisterDeclaration> _registerDeclarations = new Dictionary<RegisterKey, RegisterDeclaration>();

		public RegisterState(ShaderModel shader)
		{
			Load(shader);
		}

		public ICollection<ConstantDeclaration> ConstantDeclarations { get; private set; }

		public IDictionary<RegisterKey, RegisterDeclaration> MethodInputRegisters { get; } = new Dictionary<RegisterKey, RegisterDeclaration>();
		public IDictionary<RegisterKey, RegisterDeclaration> MethodOutputRegisters { get; } = new Dictionary<RegisterKey, RegisterDeclaration>();

		private void Load(ShaderModel shader)
		{
			ConstantDeclarations = shader.ConstantTable.ConstantDeclarations;
			foreach (var constantDeclaration in ConstantDeclarations)
			{
				RegisterType registerType;
				switch (constantDeclaration.RegisterSet)
				{
					case RegisterSet.Bool:
						registerType = RegisterType.ConstBool;
						break;
					case RegisterSet.Float4:
						registerType = RegisterType.Const;
						break;
					case RegisterSet.Int4:
						registerType = RegisterType.ConstInt;
						break;
					case RegisterSet.Sampler:
						registerType = RegisterType.Sampler;
						break;
					default:
						throw new InvalidOperationException();
				}
				if (registerType == RegisterType.Sampler)
				{
					// Use declaration from declaration instruction instead
					continue;
				}
				for (int r = 0; r < constantDeclaration.RegisterCount; r++)
				{
					var registerKey = new RegisterKey(registerType, constantDeclaration.RegisterIndex + r);
					var registerDeclaration = new RegisterDeclaration(registerKey);
					_registerDeclarations.Add(registerKey, registerDeclaration);
				}
			}

			foreach (var instruction in shader.Tokens.Where(i => i.HasDestination))
			{
				if (instruction.Opcode == Opcode.Dcl)
				{
					var registerDeclaration = new RegisterDeclaration(instruction);
					RegisterKey registerKey = registerDeclaration.RegisterKey;

					_registerDeclarations.Add(registerKey, registerDeclaration);

					switch (registerKey.Type)
					{
						case RegisterType.Input:
						case RegisterType.MiscType:
							MethodInputRegisters.Add(registerKey, registerDeclaration);
							break;
						case RegisterType.Output:
						case RegisterType.ColorOut:
							MethodOutputRegisters.Add(registerKey, registerDeclaration);
							break;
						case RegisterType.Sampler:
						case RegisterType.Addr:
							break;
						default:
							throw new Exception($"Unexpected dcl {registerKey.Type}");
					}
				}
				else if (instruction.Opcode == Opcode.Def)
				{
					var constant = new Constant(
						instruction.GetParamRegisterNumber(0),
						instruction.GetParamSingle(1),
						instruction.GetParamSingle(2),
						instruction.GetParamSingle(3),
						instruction.GetParamSingle(4));
					_constantDefinitions.Add(constant);
				}
				else if (instruction.Opcode == Opcode.DefI)
				{
					var constantInt = new ConstantInt(instruction.GetParamRegisterNumber(0),
						instruction.Data[1],
						instruction.Data[2],
						instruction.Data[3],
						instruction.Data[4]);
					_constantIntDefinitions.Add(constantInt);
				}
				else
				{
					// Find all assignments to color outputs, because pixel shader outputs are not declared.
					int destIndex = instruction.GetDestinationParamIndex();
					RegisterType registerType = instruction.GetParamRegisterType(destIndex);
					int registerNumber = instruction.GetParamRegisterNumber(destIndex);
					var registerKey = new RegisterKey(registerType, registerNumber);
					if (_registerDeclarations.ContainsKey(registerKey) == false)
					{
						var reg = new RegisterDeclaration(registerKey);
						_registerDeclarations[registerKey] = reg;
						if (registerType == RegisterType.ColorOut ||
								registerType == RegisterType.RastOut ||
								registerType == RegisterType.Output ||
								registerType == RegisterType.AttrOut)
						{
							MethodOutputRegisters[registerKey] = reg;
						}
					}

				}
			}
		}

		public string GetDestinationName(Token instruction)
		{
			int destIndex = instruction.GetDestinationParamIndex();
			RegisterKey registerKey = instruction.GetParamRegisterKey(destIndex);

			string registerName = GetRegisterName(registerKey);
			registerName = registerName ?? instruction.GetParamRegisterName(destIndex);
			var registerLength = GetRegisterFullLength(registerKey);
			string writeMaskName = instruction.GetDestinationWriteMaskName(registerLength, true);

			return string.Format("{0}{1}", registerName, writeMaskName);
		}

		public string GetSourceName(Token instruction, int srcIndex)
		{
			string sourceRegisterName;
			RegisterKey registerKey = instruction.GetParamRegisterKey(srcIndex);
			var registerType = instruction.GetParamRegisterType(srcIndex);
			switch (registerType)
			{
				case RegisterType.Const:
				case RegisterType.Const2:
				case RegisterType.Const3:
				case RegisterType.Const4:
				case RegisterType.ConstBool:
				case RegisterType.ConstInt:
					sourceRegisterName = GetSourceConstantName(instruction, srcIndex);
					if (sourceRegisterName != null)
					{
						return sourceRegisterName;
					}

					ParameterType parameterType;
					switch (registerType)
					{
						case RegisterType.Const:
						case RegisterType.Const2:
						case RegisterType.Const3:
						case RegisterType.Const4:
							parameterType = ParameterType.Float;
							break;
						case RegisterType.ConstBool:
							parameterType = ParameterType.Bool;
							break;
						case RegisterType.ConstInt:
							parameterType = ParameterType.Int;
							break;
						default:
							throw new NotImplementedException();
					}
					int registerNumber = instruction.GetParamRegisterNumber(srcIndex);
					ConstantDeclaration decl = FindConstant(registerNumber);
					if (decl == null)
					{
						// Constant register not found in def statements nor the constant table
						//TODO:
						return $"Error {registerType}{registerNumber}";
						//throw new NotImplementedException();
					}

					if (decl.ParameterClass == ParameterClass.MatrixRows)
					{
						sourceRegisterName = string.Format("{0}[{1}]", decl.Name, registerNumber - decl.RegisterIndex);
					}
					else
					{
						sourceRegisterName = decl.Name;
					}
					break;
				default:
					sourceRegisterName = GetRegisterName(registerKey);
					break;
			}

			sourceRegisterName = sourceRegisterName ?? instruction.GetParamRegisterName(srcIndex);

			sourceRegisterName += instruction.GetSourceSwizzleName(srcIndex, true);
			return ApplyModifier(instruction.GetSourceModifier(srcIndex), sourceRegisterName);
		}

		public uint GetRegisterFullLength(RegisterKey registerKey)
		{
			if (registerKey.Type == RegisterType.Const)
			{
				var constant = FindConstant(ParameterType.Float, registerKey.Number);
				return constant.Columns;
			}

			RegisterDeclaration decl = _registerDeclarations[registerKey];
			switch (decl.TypeName)
			{
				case "float":
					return 1;
				case "float2":
					return 2;
				case "float3":
					return 3;
				case "float4":
					return 4;
				default:
					throw new InvalidOperationException();
			}
		}

		public string GetRegisterName(RegisterKey registerKey)
		{
			var decl = _registerDeclarations[registerKey];
			switch (registerKey.Type)
			{
				case RegisterType.Texture:
					return decl.Name;
				case RegisterType.Input:
					return (MethodInputRegisters.Count == 1) ? decl.Name : ("i." + decl.Name);
				case RegisterType.RastOut:
				case RegisterType.Output:
				 case RegisterType.AttrOut:
				case RegisterType.ColorOut:
					return (MethodOutputRegisters.Count == 1) ? decl.Name : ("o." + decl.Name);
				case RegisterType.Const:
					var constDecl = FindConstant(ParameterType.Float, registerKey.Number);
					if (ColumnMajorOrder)
					{
						if (constDecl.Rows == 1)
						{
							return constDecl.Name;
						}
						string col = (registerKey.Number - constDecl.RegisterIndex).ToString();
						return $"transpose({constDecl.Name})[{col}]";
					}
					if (constDecl.Rows == 1)
					{
						return constDecl.Name;
					}
					string row = (registerKey.Number - constDecl.RegisterIndex).ToString();
					return constDecl.Name + $"[{row}]";
				case RegisterType.Sampler:
					ConstantDeclaration samplerDecl = FindConstant(RegisterSet.Sampler, registerKey.Number);
					if (samplerDecl != null)
					{
						return samplerDecl.Name;
					}
					else
					{
						throw new NotImplementedException();
					}
				case RegisterType.MiscType:
					switch (registerKey.Number)
					{
						case 0:
							return "vFace";
						case 1:
							return "vPos";
						default:
							throw new NotImplementedException();
					}
				case RegisterType.Temp:
					return registerKey.ToString();
				default:
					throw new NotImplementedException();
			}
		}

		public ConstantDeclaration FindConstant(RegisterInputNode register)
		{
			if (register.RegisterComponentKey.Type != RegisterType.Const)
			{
				return null;
			}
			return FindConstant(ParameterType.Float, register.RegisterComponentKey.Number);
		}

		public ConstantDeclaration FindConstant(RegisterSet set, int index)
		{
			return ConstantDeclarations.FirstOrDefault(c =>
				c.RegisterSet == set &&
				c.ContainsIndex(index));
		}

		public ConstantDeclaration FindConstant(ParameterType type, int index)
		{
			return ConstantDeclarations.FirstOrDefault(c =>
				c.ParameterType == type &&
				c.ContainsIndex(index));
		}


		public ConstantDeclaration FindConstant(int index)
		{
			return ConstantDeclarations.FirstOrDefault(c => c.ContainsIndex(index));
		}

		private string GetSourceConstantName(Token instruction, int srcIndex)
		{
			var registerType = instruction.GetParamRegisterType(srcIndex);
			int registerNumber = instruction.GetParamRegisterNumber(srcIndex);

			switch (registerType)
			{
				case RegisterType.ConstBool:
					//throw new NotImplementedException();
					return null;
				case RegisterType.ConstInt:
					{
						var constantInt = _constantIntDefinitions.FirstOrDefault(x => x.RegisterIndex == registerNumber);
						if (constantInt == null)
						{
							return null;
						}
						byte[] swizzle = instruction.GetSourceSwizzleComponents(srcIndex);
						uint[] constant = {
								constantInt[swizzle[0]],
								constantInt[swizzle[1]],
								constantInt[swizzle[2]],
								constantInt[swizzle[3]] };

						switch (instruction.GetSourceModifier(srcIndex))
						{
							case SourceModifier.None:
								break;
							case SourceModifier.Negate:
								throw new NotImplementedException();
								/*
								for (int i = 0; i < 4; i++)
								{
									constant[i] = -constant[i];
								}
								break;*/
							default:
								throw new NotImplementedException();
						}

						int destLength = instruction.GetDestinationMaskLength();
						switch (destLength)
						{
							case 1:
								return constant[0].ToString();
							case 2:
								if (constant[0] == constant[1])
								{
									return constant[0].ToString();
								}
								return $"int2({constant[0]}, {constant[1]})";
							case 3:
								if (constant[0] == constant[1] && constant[0] == constant[2])
								{
									return constant[0].ToString();
								}
								return $"int3({constant[0]}, {constant[1]}, {constant[2]})";
							case 4:
								if (constant[0] == constant[1] && constant[0] == constant[2] && constant[0] == constant[3])
								{
									return constant[0].ToString();
								}
								return $"int4({constant[0]}, {constant[1]}, {constant[2]}, {constant[3]})";
							default:
								throw new InvalidOperationException();
						}
					}

				case RegisterType.Const:
				case RegisterType.Const2:
				case RegisterType.Const3:
				case RegisterType.Const4:
					{
						var constantRegister = _constantDefinitions.FirstOrDefault(x => x.RegisterIndex == registerNumber);
						if (constantRegister == null)
						{
							return null;
						}

						byte[] swizzle = instruction.GetSourceSwizzleComponents(srcIndex);
						float[] constant = {
							constantRegister[swizzle[0]],
							constantRegister[swizzle[1]],
							constantRegister[swizzle[2]],
							constantRegister[swizzle[3]] };

						switch (instruction.GetSourceModifier(srcIndex))
						{
							case SourceModifier.None:
								break;
							case SourceModifier.Negate:
								for (int i = 0; i < 4; i++)
								{
									constant[i] = -constant[i];
								}
								break;
							default:
								throw new NotImplementedException();
						}

						int destLength;
						if (instruction.HasDestination)
						{
							destLength = instruction.GetDestinationMaskLength();
						}
						else
						{
							if (instruction.Opcode == Opcode.If || instruction.Opcode == Opcode.IfC)
							{
								// TODO
							}
							destLength = 4;
						}
						switch (destLength)
						{
							case 1:
								return constant[0].ToString(_culture);
							case 2:
								if (constant[0] == constant[1])
								{
									return constant[0].ToString(_culture);
								}
								return string.Format("float2({0}, {1})",
									constant[0].ToString(_culture),
									constant[1].ToString(_culture));
							case 3:
								if (constant[0] == constant[1] && constant[0] == constant[2])
								{
									return constant[0].ToString(_culture);
								}
								return string.Format("float3({0}, {1}, {2})",
									constant[0].ToString(_culture),
									constant[1].ToString(_culture),
									constant[2].ToString(_culture));
							case 4:
								if (constant[0] == constant[1] && constant[0] == constant[2] && constant[0] == constant[3])
								{
									return constant[0].ToString(_culture);
								}
								return string.Format("float4({0}, {1}, {2}, {3})",
									constant[0].ToString(_culture),
									constant[1].ToString(_culture),
									constant[2].ToString(_culture),
									constant[3].ToString(_culture));
							default:
								throw new InvalidOperationException();
						}
					}
				default:
					return null;
			}
		}


		private static string ApplyModifier(SourceModifier modifier, string value)
		{
			switch (modifier)
			{
				case SourceModifier.None:
					return value;
				case SourceModifier.Negate:
					return $"-{value}";
				case SourceModifier.Bias:
					return $"{value}_bias";
				case SourceModifier.BiasAndNegate:
					return $"-{value}_bias";
				case SourceModifier.Sign:
					return $"{value}_bx2";
				case SourceModifier.SignAndNegate:
					return $"-{value}_bx2";
				case SourceModifier.Complement:
					throw new NotImplementedException();
				case SourceModifier.X2:
					return $"(2 * {value})";
				case SourceModifier.X2AndNegate:
					return $"(-2 * {value})";
				case SourceModifier.DivideByZ:
					return $"{value}_dz";
				case SourceModifier.DivideByW:
					return $"{value}_dw";
				case SourceModifier.Abs:
					return $"abs({value})";
				case SourceModifier.AbsAndNegate:
					return $"-abs({value})";
				case SourceModifier.Not:
					throw new NotImplementedException();
				default:
					throw new NotImplementedException();
			}
		}
	}
}
