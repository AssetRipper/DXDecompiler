﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;
using SlimShader.VirtualMachine.Analysis.ExecutableInstructions;
using SlimShader.VirtualMachine.Execution;

namespace SlimShader.VirtualMachine.Jitter
{
    public class JitShaderExecutor : IShaderExecutor
    {
        private delegate IEnumerable<ExecutionResponse> ExecuteShaderDelegate(
            VirtualMachine virtualMachine, ExecutionContext[] executionContexts,
            ExecutableInstruction[] instructions);

        private readonly Dictionary<BytecodeContainer, ExecuteShaderDelegate> _jittedShaderCache;

        public JitShaderExecutor()
        {
            _jittedShaderCache = new Dictionary<BytecodeContainer, ExecuteShaderDelegate>();
        }

        public IEnumerable<ExecutionResponse> Execute(
            VirtualMachine virtualMachine, ExecutionContext[] executionContexts,
            ExecutableInstruction[] instructions)
        {
            // Find existing JITted shader.
            ExecuteShaderDelegate jittedShader;
            if (!_jittedShaderCache.TryGetValue(virtualMachine.Bytecode, out jittedShader))
            {
                // If shader hasn't already been JITted, JIT it now.
                jittedShader = JitCompileShader(instructions);
                _jittedShaderCache.Add(virtualMachine.Bytecode, jittedShader);
            }

            // Execute shader.
            return jittedShader(virtualMachine, executionContexts, instructions);
        }

        private static ExecuteShaderDelegate JitCompileShader(ExecutableInstruction[] instructions)
        {
            var assemblyReferences = new[]
            {
                MetadataReference.CreateAssemblyReference("mscorlib"),
                new MetadataFileReference(typeof(Number4).Assembly.Location),
                new MetadataFileReference(typeof(VirtualMachine).Assembly.Location)
            };

            const string outputName = "SlimShader.VirtualMachine.Jitter.Generated";
            var code = ShaderCodeGenerator.Generate(instructions);
            
            var compilation = Compilation.Create(outputName)
                .WithOptions(new CompilationOptions(OutputKind.DynamicallyLinkedLibrary, debugInformationKind: Roslyn.Compilers.Common.DebugInformationKind.Full))
                .AddReferences(assemblyReferences)
                .AddSyntaxTrees(SyntaxTree.ParseText(code));

            var moduleBuilder = AppDomain.CurrentDomain
                .DefineDynamicAssembly(new AssemblyName(outputName),
                    AssemblyBuilderAccess.RunAndCollect)
                .DefineDynamicModule(outputName);

            System.Diagnostics.Debug.Write(code);

            var compilationResult = compilation.Emit(moduleBuilder);
            if (!compilationResult.Success)
            {
                var errorMessage = string.Empty;
                foreach (var diagnostic in compilationResult.Diagnostics)
                    errorMessage += diagnostic + Environment.NewLine;
                throw new Exception(errorMessage);
            }

            var dynamicClass = moduleBuilder.GetType("DynamicShaderExecutor", false, true);
            var dynamicMethod = dynamicClass.GetMethod("Execute");

            return (ExecuteShaderDelegate) dynamicMethod.CreateDelegate(typeof(ExecuteShaderDelegate));
        }
    }
}