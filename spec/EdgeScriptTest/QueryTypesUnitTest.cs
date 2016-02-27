using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EdgeScriptTest
{
    [TestClass]
    public class QueryTypesUnitTest
    {
        [TestMethod]
        public void Find()
        {
            var types = Helper.Find(typeof (QueryTypesUnitTest).Assembly);
            
            Assert.IsNotNull(types);
            Assert.IsTrue(types.Any());
        }

        [TestMethod]
        public void FindSchemas()
        {
            var types = Helper.FindSchemas(typeof(QueryTypesUnitTest).Assembly, "EdgeScriptTest.Model");

            Assert.IsNotNull(types);
            Assert.IsTrue(types.Any());
        }

        [TestMethod]
        public void Startup()
        {
            var path = "..\\..\\..\\..\\..\\..\\..\\..\\..\\Campaign\\MT\\Source\\BingAds.Api\\BingAds.Api.Model\\objd\\amd64\\BingAds.Api.Model.dll";
            var task = (new Startup()).Invoke(path);

            Assert.IsNotNull(task);
            Assert.IsNotNull(task.Result);
        }
    }
}
