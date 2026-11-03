using EmberTrace.Analysis.Compare;

namespace EmberTrace.Tests.Analysis.Compare;

[TestClass]
public class MannWhitneyTests
{
    [TestMethod]
    [DataRow(new[] { 1.1, 2.3, 3.7, 4.2, 5.9, 6.4 }, new[] { 2.8, 4.9, 7.1, 8.3, 9.6, 10.2, 11.5 },
        0.03831876314941356)]
    [DataRow(new[] { 1.0, 2, 2, 3, 3, 3, 4 }, new[] { 2.0, 3, 4, 4, 5, 5, 6, 7 }, 0.025279736885893914)]
    [DataRow(new[] { 10.0, 10.5, 11.0, 10.2 }, new[] { 20.0, 21.0, 19.5, 20.5, 22.0 }, 0.019964453305216043)]
    public void TwoSidedP_MatchesScipyReference(double[] x, double[] y, double expected)
    {
        Assert.AreEqual(expected, MannWhitney.TwoSidedP(x, y), 1e-9);
    }

    [TestMethod]
    public void TwoSidedP_IsSymmetricInItsArguments()
    {
        double[] x = [1, 5, 9, 12];
        double[] y = [2, 3, 20, 25, 30];

        Assert.AreEqual(MannWhitney.TwoSidedP(x, y), MannWhitney.TwoSidedP(y, x), 1e-12);
    }

    [TestMethod]
    public void TwoSidedP_AllValuesEqual_IsOne()
    {
        Assert.AreEqual(1.0, MannWhitney.TwoSidedP([5, 5, 5], [5, 5, 5, 5]));
    }

    [TestMethod]
    public void TwoSidedP_EmptySide_IsOne()
    {
        Assert.AreEqual(1.0, MannWhitney.TwoSidedP([], [1, 2, 3]));
    }

    [TestMethod]
    public void TwoSidedP_ExtremeSeparation_StaysInUnitInterval()
    {
        var x = Enumerable.Range(0, 200).Select(i => (double)i).ToArray();
        var y = Enumerable.Range(1000, 200).Select(i => (double)i).ToArray();

        var p = MannWhitney.TwoSidedP(x, y);

        Assert.IsGreaterThanOrEqualTo(0.0, p);
        Assert.IsLessThan(1e-30, p);
    }
}