﻿
using System;
using System.Reflection;
using System.Runtime.Serialization;

namespace dexih.transforms.Transforms
{
    [DataContract]
    public class TransformReference: TransformAttribute
    {
        [DataMember(Order = 3)]
        public string TransformClassName { get; set; }

        [DataMember(Order = 4)]
        public string TransformAssemblyName { get; set; }
        
        public Type GetTransformType()
        {
            Type type;
            if (string.IsNullOrEmpty(TransformAssemblyName))
            {
                type = Assembly.GetExecutingAssembly().GetType(TransformClassName);
            }
            else
            {
                var assembly = Assembly.Load(TransformAssemblyName);

                if (assembly == null)
                {
                    throw new TransformnNotFoundException($"The assembly {TransformClassName} was not found.");
                }
                type = assembly.GetType(TransformClassName);
            }

            return type;
        }
        
        public Transform GetTransform()
        {
            var type = GetTransformType();
            var obj = (Transform) Activator.CreateInstance(type);
            return obj;
        }
    }
}