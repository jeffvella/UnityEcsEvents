using System;
using NUnit.Framework;

namespace Vella.Tests.Attributes
{
#if UNITY_DOTSPLAYER
    public class StandaloneFixmeAttribute : IgnoreAttribute
    {
        public StandaloneFixmeAttribute() : base("Need to fix for Tiny.")
        {
        }
    }
#else
    public class StandaloneFixmeAttribute : Attribute
    {
    }
#endif
}
