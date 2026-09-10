using System;

namespace Unity.Burst
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Method)]
    public sealed class BurstCompileAttribute : Attribute
    {
    }
}
