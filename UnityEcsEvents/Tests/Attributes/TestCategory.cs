using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vella.Tests.Attributes
{
    public enum TestCategory
    {
        None = 0,
        Functionality,
        Integrity,
        Performance,
        Compatibility,
    }

    public class TestCategoryAttribute : CategoryAttribute
    {
        public TestCategoryAttribute(TestCategory functionality) : base(functionality.ToString())
        {

        }
    }
}
