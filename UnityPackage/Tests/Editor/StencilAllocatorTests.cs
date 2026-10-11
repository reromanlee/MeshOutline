using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace reromanlee.MeshOutline.Tests
{
    public class StencilAllocatorTests : OutlineTestBase
    {
        [Test]
        public void UsableValues_NeverCollideWithDeferredStencilBits()
        {
            int[] usable = Enumerable.Range(0, 256).Where(StencilAllocator.IsUsable).ToArray();
            Assert.AreEqual(StencilAllocator.Capacity, usable.Length);
            Assert.IsTrue(usable.All(v => (v & 0x0F) != 0));
            for (int i = 0; i < usable.Length; i++) Assert.AreEqual(usable[i], StencilAllocator.ValueAt(i));
        }

        [Test]
        public void EnabledOutlines_GetUniqueReferences_AndFreedOnesAreReused()
        {
            var outlines = new List<ObjectOutline>();
            for (int i = 0; i < 20; i++) outlines.Add(CreateOutlined("O" + i).GetComponent<ObjectOutline>());

            int[] refs = outlines.Select(o => o.StencilRef).ToArray();
            CollectionAssert.AllItemsAreUnique(refs);
            Assert.IsTrue(refs.All(StencilAllocator.IsUsable));

            int freed = outlines[3].StencilRef;
            outlines[3].enabled = false;
            Assert.AreEqual(0, outlines[3].StencilRef);
            ObjectOutline next = CreateOutlined("Next").GetComponent<ObjectOutline>();
            Assert.AreEqual(freed, next.StencilRef);
        }
    }
}
