using System;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfObjectAllocatorTests
{
    [Fact]
    public void Allocate_ReturnsSequentialObjectNumbersStartingAtOne()
    {
        var alloc = new PdfObjectAllocator();
        alloc.Allocate().Should().Be(1);
        alloc.Allocate().Should().Be(2);
        alloc.Allocate().Should().Be(3);
        alloc.Count.Should().Be(3);
    }

    [Fact]
    public void RecordOffset_ForAllocatedObject_IsRetrievable()
    {
        var alloc = new PdfObjectAllocator();
        int obj = alloc.Allocate();

        alloc.RecordOffset(obj, 1234);

        alloc.TryGetOffset(obj, out long offset).Should().BeTrue();
        offset.Should().Be(1234);
    }

    [Fact]
    public void RecordOffset_ForNeverAllocatedObject_ThrowsInsteadOfSilentlyAccepting()
    {
        var alloc = new PdfObjectAllocator();
        alloc.Allocate();
        const int neverAllocated = 5;

        var act = () => alloc.RecordOffset(neverAllocated, 999);

        act.Should().Throw<InvalidOperationException>(
            "an offset for an object number nothing ever allocated must fail loudly, not produce a silently wrong " +
            "xref entry the way a forgotten hand-incremented object counter misnumbered every later object");
    }

    [Fact]
    public void RecordOffset_CalledTwiceForSameObject_ThrowsInsteadOfSilentlyOverwriting()
    {
        var alloc = new PdfObjectAllocator();
        int obj = alloc.Allocate();
        alloc.RecordOffset(obj, 100);

        var act = () => alloc.RecordOffset(obj, 200);

        act.Should().Throw<InvalidOperationException>(
            "two writes claiming the same object number indicates a miscounted allocation, which must not pass silently");
    }

    [Fact]
    public void TryGetOffset_ForAllocatedButUnwrittenObject_ReturnsFalse()
    {
        var alloc = new PdfObjectAllocator();
        int obj = alloc.Allocate();

        alloc.TryGetOffset(obj, out _).Should().BeFalse(
            "an object number can be reserved before its offset is known, e.g. across a multi-object font write");
    }

    [Fact]
    public void ManyAllocations_EachOffsetIsIndependentlyCorrect()
    {
        var alloc = new PdfObjectAllocator();
        const int objectsInALargeDocument = 500;
        for (int i = 1; i <= objectsInALargeDocument; i++)
        {
            int obj = alloc.Allocate();
            obj.Should().Be(i);
            alloc.RecordOffset(obj, i * 37L);
        }

        for (int i = 1; i <= objectsInALargeDocument; i++)
        {
            alloc.TryGetOffset(i, out long offset).Should().BeTrue();
            offset.Should().Be(i * 37L, "no offset collides with or leaks into another object's slot");
        }
    }
}
