﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

using theori.Graphics.OpenGL;

namespace theori.Graphics
{
    public enum BlendMode
    {
        Normal,
        Additive,
        Multiply,
    }

    public enum BuiltInParams : uint
    {
        World,
        Projection,
        Camera,
        BillboardMatrix,
        Viewport,
        AspectRatio,
        Time,
        __BuiltInCount,
        User = 0x100,
    }

    public class MaterialParams
    {
        public readonly Dictionary<string, MaterialParam> Params = new Dictionary<string, MaterialParam>();

        public object this[string name]
        {
            get
            {
                if (Params.TryGetValue(name, out var param))
                    return param.Value;
                return null;
            }

            set
            {
                if (Params.TryGetValue(name, out var param))
                    param.Value = value;
                else Params[name] = new MaterialParam(value);
            }
        }

        public MaterialParams Copy()
        {
            var result = new MaterialParams();
            foreach (var entry in Params)
                result.Params[entry.Key] = entry.Value.Copy();
            return result;
        }
    }

    public class Material : Disposable
    {
        private static readonly Dictionary<Type, object> BindFuncs = new Dictionary<Type, object>();
        private static Action<ShaderProgram, int, T> GetBindFunc<T>()
        {
            return (Action<ShaderProgram, int, T>)BindFuncs[typeof(T)];
        }

        static Material()
        {
            void AddBindFunc<T>(Action<ShaderProgram, int, T> bind)
            {
                BindFuncs[typeof(T)] = bind;
            }
            
            AddBindFunc<int>(BindShaderVar_Int);
            AddBindFunc<float>(BindShaderVar_Float);
            AddBindFunc<Transform>(BindShaderVar_Transform);
            AddBindFunc<Vector2>(BindShaderVar_Vector2);
            AddBindFunc<Vector3>(BindShaderVar_Vector3);
            AddBindFunc<Vector4>(BindShaderVar_Vector4);
        }

        private static void BindShaderVar_Int      (ShaderProgram program, int location, int       value) => program.SetUniform(location, value);
        private static void BindShaderVar_Float    (ShaderProgram program, int location, float     value) => program.SetUniform(location, value);
        private static void BindShaderVar_Transform(ShaderProgram program, int location, Transform value) => program.SetUniform(location, (Matrix4x4)value);
        private static void BindShaderVar_Vector2  (ShaderProgram program, int location, Vector2   value) => program.SetUniform(location, value);
        private static void BindShaderVar_Vector3  (ShaderProgram program, int location, Vector3   value) => program.SetUniform(location, value);
        private static void BindShaderVar_Vector4  (ShaderProgram program, int location, Vector4   value) => program.SetUniform(location, value);

        struct BoundParamInfo
        {
            public ShaderStage Stage;
            public int Location;
            public GLType Type;
        }

        internal static Material CreateUninitialized() => new Material();

        private ProgramPipeline m_pipeline;

        public BlendMode BlendMode;
        public bool Opaque;

        private readonly ShaderProgram[] m_shaderPrograms = new ShaderProgram[3];

        private uint m_userId = (uint)BuiltInParams.User;

        private readonly Dictionary<string, uint> m_mappedParams = new Dictionary<string, uint>();
        private readonly Dictionary<uint, List<BoundParamInfo>> m_boundParams = new Dictionary<uint, List<BoundParamInfo>>();
        private readonly Dictionary<string, uint> m_textureIDs = new Dictionary<string, uint>();

        private uint m_textureID = 0;

        private Material()
        {
        }

        public Material(Stream vertexShaderStream, Stream fragmentShaderStream, Stream geometryShaderStream = null)
        {
            m_pipeline = new ProgramPipeline();

            void AddShader(Stream stream, ShaderType type)
            {
                using var streamReader = new StreamReader(stream);
                string source = streamReader.ReadToEnd();

                var program = new ShaderProgram(type, source);
                if (!program || !program.Linked)
                    Logger.Log(program.InfoLog);
                else AssignShader(program);
            }

            AddShader(vertexShaderStream, ShaderType.Vertex);
            AddShader(fragmentShaderStream, ShaderType.Fragment);
            if (geometryShaderStream != null)
                AddShader(geometryShaderStream, ShaderType.Geometry);
        }

        internal void CreatePipeline()
        {
            m_pipeline = new ProgramPipeline();
        }

        private int GetShaderIndex(ShaderStage stage)
        {
            return stage switch
            {
                ShaderStage.Vertex => 0,
                ShaderStage.Geometry => 1,
                ShaderStage.Fragment => 2,

                _ => -1,
            };
        }

        public void AssignShader(ShaderProgram program)
        {
            m_shaderPrograms[GetShaderIndex(program.Stage)] = program;

            var uniforms = program.ActiveUniforms;
            for (int count = uniforms.Length, i = 0; i < count; i++)
            {
                var info = uniforms[i];

                if (info.Type == GLType.Sampler2D)
                {
					if(!m_textureIDs.ContainsKey(info.Name))
						m_textureIDs[info.Name] = m_textureID++;
                }

                uint target = 0;
                if (Enum.TryParse<BuiltInParams>(info.Name, out var builtInKind)
                    && builtInKind < BuiltInParams.__BuiltInCount)
                {
                    target = (uint)builtInKind;
                }
                else if (!m_mappedParams.TryGetValue(info.Name, out target))
                    m_mappedParams[info.Name] = target = m_userId++;

                var paramInfo = new BoundParamInfo()
                {
                    Stage = program.Stage,
                    Location = info.Location,
                    Type = info.Type,
                };

                if (!m_boundParams.TryGetValue(target, out var list))
                    m_boundParams[target] = list = new List<BoundParamInfo>();
                list.Add(paramInfo);

#if DEBUG
                Logger.Log($"Uniform [{ i }, loc={ info.Location }, { info.Type }] = { info.Name }");
#endif
            }

            program.Use(m_pipeline);
        }

        public void Bind(RenderState state, MaterialParams p)
        {
            BindAll(BuiltInParams.Projection, state.ProjectionMatrix);
            BindAll(BuiltInParams.Camera, state.CameraMatrix);

            ApplyParams(p, state.WorldTransform);
            BindToContext();
        }

        public void BindToContext()
        {
            m_pipeline.Bind();
        }

        public void ApplyParams(MaterialParams p, Transform world)
        {
            BindAll(BuiltInParams.World, world);
            foreach (var x in p.Params)
            {
                string name = x.Key;
                var param = x.Value;

                switch (param.Type)
                {
                    case GLType.Int: BindAll(name, param.Get<int>()); break;
                    case GLType.Float: BindAll(name, param.Get<float>()); break;
                    case GLType.FloatMat4: BindAll(name, param.Get<Matrix4x4>()); break;
                    case GLType.FloatVec2: BindAll(name, param.Get<Vector2>()); break;
                    case GLType.FloatVec3: BindAll(name, param.Get<Vector3>()); break;
                    case GLType.FloatVec4: BindAll(name, param.Get<Vector4>()); break;

                    case GLType.Sampler2D:
                        if (!m_textureIDs.TryGetValue(name, out uint unit))
                        {
                            // TODO(local): error!!!
                            break;
                        }

                        param.Get<Texture>().Bind(unit);
                        BindAll(name, (int)unit);
                        break;
                }
            }
        }

        private List<BoundParamInfo> GetBoundParams(string name, out int count)
        {
            count = 0;
            if (!m_mappedParams.TryGetValue(name, out uint mId))
                return null;
            return GetBoundParams((BuiltInParams)mId, out count);
        }

        private List<BoundParamInfo> GetBoundParams(BuiltInParams p, out int count)
        {
            count = 0;
            if (!m_boundParams.TryGetValue((uint)p, out var result))
                return result;
            count = result.Count;
            return result;
        }

        private void BindAll<T>(string name, T value)
        {
            var p = GetBoundParams(name, out int count);
            BindAll(p, count, value);
        }

        private void BindAll<T>(BuiltInParams builtIn, T value)
        {
            var p = GetBoundParams(builtIn, out int count);
            BindAll(p, count, value);
        }

        private void BindAll<T>(List<BoundParamInfo> p, int count, T value)
        {
            var bind = GetBindFunc<T>();
            for (int i = 0; i < count; i++)
            {
                var param = p[i];
                
                var program = m_shaderPrograms[GetShaderIndex(param.Stage)];
                int location = param.Location;
                
                bind(program, location, value);
            }
        }

        protected override void DisposeManaged()
        {
            for (int i = 0; i < 3; i++)
                m_shaderPrograms[i]?.Dispose();
            m_pipeline.Dispose();
        }
    }
}
