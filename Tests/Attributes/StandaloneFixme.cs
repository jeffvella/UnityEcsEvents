using System;
using NUnit.Framework;

namespace Vella.Events.Tests
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
