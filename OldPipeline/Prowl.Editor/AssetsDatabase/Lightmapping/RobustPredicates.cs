// Robust adaptive-precision geometric predicates (orient3d, insphere).
//
// These return the EXACT SIGN of the orientation and in-sphere determinants using adaptive-precision
// floating-point arithmetic: a fast filter handles the common well-conditioned case and falls back to
// multi-term expansion arithmetic only when the determinant is too close to zero to trust a plain
// double. The arithmetic primitives (error-free two-sum / two-product splitting), the expansion sum /
// scale routines, the error-bound initialisation and the staged determinant evaluation implement the
// standard adaptive-precision technique; its arithmetic macros are expanded here into equivalent C#
// helper methods that return their results through `out` parameters.
//
// Used by the light-probe tetrahedralizer to get exact-sign orient3d / insphere, so near-regular
// probe grids (cospherical / coplanar degeneracies) no longer collapse the Delaunay output.

using System;

namespace Prowl.Editor.Lightmapping;

/// <summary>
/// Exact-sign 3D orientation and in-sphere predicates. All inputs are 3 doubles per point.
/// Thread-safe (stateless apart from the read-only error bounds computed once in the static ctor).
/// </summary>
internal static class RobustPredicates
{
    // Error bounds + the splitter, computed once by ExactInit.
    private static readonly double splitter;     // = 2^ceil(p/2)+1, used to split a double into two halves
    private static readonly double resulterrbound;
    private static readonly double o3derrboundA, o3derrboundB, o3derrboundC;
    private static readonly double isperrboundA, isperrboundB, isperrboundC;

    static RobustPredicates()
    {
        // exactinit(): find machine epsilon + the splitter by repeated halving, then the per-predicate
        // a-priori error bounds. Faithful to predicates.c:exactinit().
        double half = 0.5;
        double epsilon = 1.0;
        double splt = 1.0;
        double check = 1.0;
        bool everyOther = true;
        do
        {
            double lastcheck = check;
            epsilon *= half;
            if (everyOther) splt *= 2.0;
            everyOther = !everyOther;
            check = 1.0 + epsilon;
            if (check == lastcheck) break;
        } while (check != 1.0);
        splt += 1.0;

        splitter = splt;
        resulterrbound = (3.0 + 8.0 * epsilon) * epsilon;
        o3derrboundA = (7.0 + 56.0 * epsilon) * epsilon;
        o3derrboundB = (3.0 + 28.0 * epsilon) * epsilon;
        o3derrboundC = (26.0 + 288.0 * epsilon) * epsilon * epsilon;
        isperrboundA = (16.0 + 224.0 * epsilon) * epsilon;
        isperrboundB = (5.0 + 72.0 * epsilon) * epsilon;
        isperrboundC = (71.0 + 1408.0 * epsilon) * epsilon * epsilon;
    }

    // ---- arithmetic primitives (error-free two-sum / two-diff / split / two-product) ----

    private static void FastTwoSum(double a, double b, out double x, out double y)
    {
        x = a + b;
        double bvirt = x - a;
        y = b - bvirt;
    }

    private static void TwoSum(double a, double b, out double x, out double y)
    {
        x = a + b;
        double bvirt = x - a;
        double avirt = x - bvirt;
        double bround = b - bvirt;
        double around = a - avirt;
        y = around + bround;
    }

    private static void TwoDiff(double a, double b, out double x, out double y)
    {
        x = a - b;
        double bvirt = a - x;
        double avirt = x + bvirt;
        double bround = bvirt - b;
        double around = a - avirt;
        y = around + bround;
    }

    // Two_Diff_Tail: the round-off of an already-computed difference x = a - b.
    private static double TwoDiffTail(double a, double b, double x)
    {
        double bvirt = a - x;
        double avirt = x + bvirt;
        double bround = bvirt - b;
        double around = a - avirt;
        return around + bround;
    }

    private static void Split(double a, out double ahi, out double alo)
    {
        double c = splitter * a;
        double abig = c - a;
        ahi = c - abig;
        alo = a - ahi;
    }

    private static void TwoProduct(double a, double b, out double x, out double y)
    {
        x = a * b;
        Split(a, out double ahi, out double alo);
        Split(b, out double bhi, out double blo);
        double err1 = x - (ahi * bhi);
        double err2 = err1 - (alo * bhi);
        double err3 = err2 - (ahi * blo);
        y = (alo * blo) - err3;
    }

    private static void TwoProductPresplit(double a, double b, double bhi, double blo, out double x, out double y)
    {
        x = a * b;
        Split(a, out double ahi, out double alo);
        double err1 = x - (ahi * bhi);
        double err2 = err1 - (alo * bhi);
        double err3 = err2 - (ahi * blo);
        y = (alo * blo) - err3;
    }

    // Two_One_Diff: (a1,a0) - b  ->  (x2,x1,x0)
    private static void TwoOneDiff(double a1, double a0, double b, out double x2, out double x1, out double x0)
    {
        TwoDiff(a0, b, out double i, out x0);
        TwoSum(a1, i, out x2, out x1);
    }

    // Two_Two_Diff: (a1,a0) - (b1,b0)  ->  (x3,x2,x1,x0)
    private static void TwoTwoDiff(double a1, double a0, double b1, double b0,
                                   out double x3, out double x2, out double x1, out double x0)
    {
        TwoOneDiff(a1, a0, b0, out double j, out double _0, out x0);
        TwoOneDiff(j, _0, b1, out x3, out x2, out x1);
    }

    // Two_One_Product: (a1,a0) * b  ->  (x3,x2,x1,x0)
    private static void TwoOneProduct(double a1, double a0, double b,
                                      out double x3, out double x2, out double x1, out double x0)
    {
        Split(b, out double bhi, out double blo);
        TwoProductPresplit(a0, b, bhi, blo, out double i, out x0);
        TwoProductPresplit(a1, b, bhi, blo, out double j, out double _0);
        TwoSum(i, _0, out double k, out x1);
        FastTwoSum(j, k, out x3, out x2);
    }

    // ---- expansion routines ----

    // fast_expansion_sum_zeroelim: h = e + f, components in increasing magnitude, zeros eliminated.
    private static int FastExpansionSumZeroElim(int elen, ReadOnlySpan<double> e, int flen, ReadOnlySpan<double> f, Span<double> h)
    {
        double q, qnew, hh;
        int eindex, findex, hindex;
        double enow, fnow;

        enow = e[0];
        fnow = f[0];
        eindex = findex = 0;
        if ((fnow > enow) == (fnow > -enow)) { q = enow; enow = e[++eindex]; }
        else { q = fnow; fnow = f[++findex]; }
        hindex = 0;
        if ((eindex < elen) && (findex < flen))
        {
            if ((fnow > enow) == (fnow > -enow)) { FastTwoSum(enow, q, out qnew, out hh); enow = e[++eindex]; }
            else { FastTwoSum(fnow, q, out qnew, out hh); fnow = f[++findex]; }
            q = qnew;
            if (hh != 0.0) h[hindex++] = hh;
            while ((eindex < elen) && (findex < flen))
            {
                if ((fnow > enow) == (fnow > -enow)) { TwoSum(q, enow, out qnew, out hh); enow = e[++eindex]; }
                else { TwoSum(q, fnow, out qnew, out hh); fnow = f[++findex]; }
                q = qnew;
                if (hh != 0.0) h[hindex++] = hh;
            }
        }
        while (eindex < elen)
        {
            TwoSum(q, enow, out qnew, out hh);
            enow = e[++eindex];
            q = qnew;
            if (hh != 0.0) h[hindex++] = hh;
        }
        while (findex < flen)
        {
            TwoSum(q, fnow, out qnew, out hh);
            fnow = f[++findex];
            q = qnew;
            if (hh != 0.0) h[hindex++] = hh;
        }
        if ((q != 0.0) || (hindex == 0)) h[hindex++] = q;
        return hindex;
    }

    // scale_expansion_zeroelim: h = e * b, zeros eliminated.
    private static int ScaleExpansionZeroElim(int elen, ReadOnlySpan<double> e, double b, Span<double> h)
    {
        Split(b, out double bhi, out double blo);
        TwoProductPresplit(e[0], b, bhi, blo, out double q, out double hh);
        int hindex = 0;
        if (hh != 0) h[hindex++] = hh;
        for (int eindex = 1; eindex < elen; eindex++)
        {
            double enow = e[eindex];
            TwoProductPresplit(enow, b, bhi, blo, out double product1, out double product0);
            TwoSum(q, product0, out double sum, out hh);
            if (hh != 0) h[hindex++] = hh;
            FastTwoSum(product1, sum, out q, out hh);
            if (hh != 0) h[hindex++] = hh;
        }
        if ((q != 0.0) || (hindex == 0)) h[hindex++] = q;
        return hindex;
    }

    private static double Estimate(int elen, ReadOnlySpan<double> e)
    {
        double q = e[0];
        for (int i = 1; i < elen; i++) q += e[i];
        return q;
    }

    // ---- orient3d ----

    /// <summary>
    /// Returns a positive value if point d lies below the plane through a,b,c (i.e. a,b,c appear
    /// counterclockwise when viewed from above d), negative if above, zero if coplanar. Exact sign.
    /// Each point is a 3-element span (x,y,z).
    /// </summary>
    public static double Orient3D(ReadOnlySpan<double> pa, ReadOnlySpan<double> pb, ReadOnlySpan<double> pc, ReadOnlySpan<double> pd)
    {
        double adx = pa[0] - pd[0];
        double bdx = pb[0] - pd[0];
        double cdx = pc[0] - pd[0];
        double ady = pa[1] - pd[1];
        double bdy = pb[1] - pd[1];
        double cdy = pc[1] - pd[1];
        double adz = pa[2] - pd[2];
        double bdz = pb[2] - pd[2];
        double cdz = pc[2] - pd[2];

        double bdxcdy = bdx * cdy;
        double cdxbdy = cdx * bdy;
        double cdxady = cdx * ady;
        double adxcdy = adx * cdy;
        double adxbdy = adx * bdy;
        double bdxady = bdx * ady;

        double det = adz * (bdxcdy - cdxbdy)
                   + bdz * (cdxady - adxcdy)
                   + cdz * (adxbdy - bdxady);

        double permanent = (Math.Abs(bdxcdy) + Math.Abs(cdxbdy)) * Math.Abs(adz)
                         + (Math.Abs(cdxady) + Math.Abs(adxcdy)) * Math.Abs(bdz)
                         + (Math.Abs(adxbdy) + Math.Abs(bdxady)) * Math.Abs(cdz);
        double errbound = o3derrboundA * permanent;
        if ((det > errbound) || (-det > errbound))
            return det;

        return Orient3DAdapt(pa, pb, pc, pd, permanent);
    }

    private static double Orient3DAdapt(ReadOnlySpan<double> pa, ReadOnlySpan<double> pb, ReadOnlySpan<double> pc, ReadOnlySpan<double> pd, double permanent)
    {
        double det, errbound;

        Span<double> bc = stackalloc double[4];
        Span<double> ca = stackalloc double[4];
        Span<double> ab = stackalloc double[4];
        Span<double> adet = stackalloc double[8];
        Span<double> bdet = stackalloc double[8];
        Span<double> cdet = stackalloc double[8];
        Span<double> abdet = stackalloc double[16];
        Span<double> fin1 = stackalloc double[192];
        Span<double> fin2 = stackalloc double[192];

        double adx = pa[0] - pd[0];
        double bdx = pb[0] - pd[0];
        double cdx = pc[0] - pd[0];
        double ady = pa[1] - pd[1];
        double bdy = pb[1] - pd[1];
        double cdy = pc[1] - pd[1];
        double adz = pa[2] - pd[2];
        double bdz = pb[2] - pd[2];
        double cdz = pc[2] - pd[2];

        TwoProduct(bdx, cdy, out double bdxcdy1, out double bdxcdy0);
        TwoProduct(cdx, bdy, out double cdxbdy1, out double cdxbdy0);
        TwoTwoDiff(bdxcdy1, bdxcdy0, cdxbdy1, cdxbdy0, out double bc3, out double bc2, out double bc1, out double bc0);
        bc[0] = bc0; bc[1] = bc1; bc[2] = bc2; bc[3] = bc3;
        int alen = ScaleExpansionZeroElim(4, bc, adz, adet);

        TwoProduct(cdx, ady, out double cdxady1, out double cdxady0);
        TwoProduct(adx, cdy, out double adxcdy1, out double adxcdy0);
        TwoTwoDiff(cdxady1, cdxady0, adxcdy1, adxcdy0, out double ca3, out double ca2, out double ca1, out double ca0);
        ca[0] = ca0; ca[1] = ca1; ca[2] = ca2; ca[3] = ca3;
        int blen = ScaleExpansionZeroElim(4, ca, bdz, bdet);

        TwoProduct(adx, bdy, out double adxbdy1, out double adxbdy0);
        TwoProduct(bdx, ady, out double bdxady1, out double bdxady0);
        TwoTwoDiff(adxbdy1, adxbdy0, bdxady1, bdxady0, out double ab3, out double ab2, out double ab1, out double ab0);
        ab[0] = ab0; ab[1] = ab1; ab[2] = ab2; ab[3] = ab3;
        int clen = ScaleExpansionZeroElim(4, ab, cdz, cdet);

        int ablen = FastExpansionSumZeroElim(alen, adet, blen, bdet, abdet);
        int finlength = FastExpansionSumZeroElim(ablen, abdet, clen, cdet, fin1);

        det = Estimate(finlength, fin1);
        errbound = o3derrboundB * permanent;
        if ((det >= errbound) || (-det >= errbound))
            return det;

        double adxtail = TwoDiffTail(pa[0], pd[0], adx);
        double bdxtail = TwoDiffTail(pb[0], pd[0], bdx);
        double cdxtail = TwoDiffTail(pc[0], pd[0], cdx);
        double adytail = TwoDiffTail(pa[1], pd[1], ady);
        double bdytail = TwoDiffTail(pb[1], pd[1], bdy);
        double cdytail = TwoDiffTail(pc[1], pd[1], cdy);
        double adztail = TwoDiffTail(pa[2], pd[2], adz);
        double bdztail = TwoDiffTail(pb[2], pd[2], bdz);
        double cdztail = TwoDiffTail(pc[2], pd[2], cdz);

        if ((adxtail == 0.0) && (bdxtail == 0.0) && (cdxtail == 0.0)
            && (adytail == 0.0) && (bdytail == 0.0) && (cdytail == 0.0)
            && (adztail == 0.0) && (bdztail == 0.0) && (cdztail == 0.0))
            return det;

        errbound = o3derrboundC * permanent + resulterrbound * Math.Abs(det);
        det += (adz * ((bdx * cdytail + cdy * bdxtail) - (bdy * cdxtail + cdx * bdytail))
                + adztail * (bdx * cdy - bdy * cdx))
             + (bdz * ((cdx * adytail + ady * cdxtail) - (cdy * adxtail + adx * cdytail))
                + bdztail * (cdx * ady - cdy * adx))
             + (cdz * ((adx * bdytail + bdy * adxtail) - (ady * bdxtail + bdx * adytail))
                + cdztail * (adx * bdy - ady * bdx));
        if ((det >= errbound) || (-det >= errbound))
            return det;

        Span<double> finnow = fin1;
        Span<double> finother = fin2;

        Span<double> at_b = stackalloc double[4];
        Span<double> at_c = stackalloc double[4];
        Span<double> bt_c = stackalloc double[4];
        Span<double> bt_a = stackalloc double[4];
        Span<double> ct_a = stackalloc double[4];
        Span<double> ct_b = stackalloc double[4];
        int at_blen, at_clen, bt_clen, bt_alen, ct_alen, ct_blen;

        double negate;

        if (adxtail == 0.0)
        {
            if (adytail == 0.0)
            {
                at_b[0] = 0.0; at_blen = 1;
                at_c[0] = 0.0; at_clen = 1;
            }
            else
            {
                negate = -adytail;
                TwoProduct(negate, bdx, out double at_blarge, out double v0); at_b[0] = v0; at_b[1] = at_blarge; at_blen = 2;
                TwoProduct(adytail, cdx, out double at_clarge, out double v1); at_c[0] = v1; at_c[1] = at_clarge; at_clen = 2;
            }
        }
        else
        {
            if (adytail == 0.0)
            {
                TwoProduct(adxtail, bdy, out double at_blarge, out double v0); at_b[0] = v0; at_b[1] = at_blarge; at_blen = 2;
                negate = -adxtail;
                TwoProduct(negate, cdy, out double at_clarge, out double v1); at_c[0] = v1; at_c[1] = at_clarge; at_clen = 2;
            }
            else
            {
                TwoProduct(adxtail, bdy, out double adxt_bdy1, out double adxt_bdy0);
                TwoProduct(adytail, bdx, out double adyt_bdx1, out double adyt_bdx0);
                TwoTwoDiff(adxt_bdy1, adxt_bdy0, adyt_bdx1, adyt_bdx0, out double at_blarge, out double b2, out double b1, out double b0);
                at_b[0] = b0; at_b[1] = b1; at_b[2] = b2; at_b[3] = at_blarge; at_blen = 4;
                TwoProduct(adytail, cdx, out double adyt_cdx1, out double adyt_cdx0);
                TwoProduct(adxtail, cdy, out double adxt_cdy1, out double adxt_cdy0);
                TwoTwoDiff(adyt_cdx1, adyt_cdx0, adxt_cdy1, adxt_cdy0, out double at_clarge, out double c2, out double c1, out double c0);
                at_c[0] = c0; at_c[1] = c1; at_c[2] = c2; at_c[3] = at_clarge; at_clen = 4;
            }
        }
        if (bdxtail == 0.0)
        {
            if (bdytail == 0.0)
            {
                bt_c[0] = 0.0; bt_clen = 1;
                bt_a[0] = 0.0; bt_alen = 1;
            }
            else
            {
                negate = -bdytail;
                TwoProduct(negate, cdx, out double bt_clarge, out double v0); bt_c[0] = v0; bt_c[1] = bt_clarge; bt_clen = 2;
                TwoProduct(bdytail, adx, out double bt_alarge, out double v1); bt_a[0] = v1; bt_a[1] = bt_alarge; bt_alen = 2;
            }
        }
        else
        {
            if (bdytail == 0.0)
            {
                TwoProduct(bdxtail, cdy, out double bt_clarge, out double v0); bt_c[0] = v0; bt_c[1] = bt_clarge; bt_clen = 2;
                negate = -bdxtail;
                TwoProduct(negate, ady, out double bt_alarge, out double v1); bt_a[0] = v1; bt_a[1] = bt_alarge; bt_alen = 2;
            }
            else
            {
                TwoProduct(bdxtail, cdy, out double bdxt_cdy1, out double bdxt_cdy0);
                TwoProduct(bdytail, cdx, out double bdyt_cdx1, out double bdyt_cdx0);
                TwoTwoDiff(bdxt_cdy1, bdxt_cdy0, bdyt_cdx1, bdyt_cdx0, out double bt_clarge, out double c2, out double c1, out double c0);
                bt_c[0] = c0; bt_c[1] = c1; bt_c[2] = c2; bt_c[3] = bt_clarge; bt_clen = 4;
                TwoProduct(bdytail, adx, out double bdyt_adx1, out double bdyt_adx0);
                TwoProduct(bdxtail, ady, out double bdxt_ady1, out double bdxt_ady0);
                TwoTwoDiff(bdyt_adx1, bdyt_adx0, bdxt_ady1, bdxt_ady0, out double bt_alarge, out double a2, out double a1, out double a0);
                bt_a[0] = a0; bt_a[1] = a1; bt_a[2] = a2; bt_a[3] = bt_alarge; bt_alen = 4;
            }
        }
        if (cdxtail == 0.0)
        {
            if (cdytail == 0.0)
            {
                ct_a[0] = 0.0; ct_alen = 1;
                ct_b[0] = 0.0; ct_blen = 1;
            }
            else
            {
                negate = -cdytail;
                TwoProduct(negate, adx, out double ct_alarge, out double v0); ct_a[0] = v0; ct_a[1] = ct_alarge; ct_alen = 2;
                TwoProduct(cdytail, bdx, out double ct_blarge, out double v1); ct_b[0] = v1; ct_b[1] = ct_blarge; ct_blen = 2;
            }
        }
        else
        {
            if (cdytail == 0.0)
            {
                TwoProduct(cdxtail, ady, out double ct_alarge, out double v0); ct_a[0] = v0; ct_a[1] = ct_alarge; ct_alen = 2;
                negate = -cdxtail;
                TwoProduct(negate, bdy, out double ct_blarge, out double v1); ct_b[0] = v1; ct_b[1] = ct_blarge; ct_blen = 2;
            }
            else
            {
                TwoProduct(cdxtail, ady, out double cdxt_ady1, out double cdxt_ady0);
                TwoProduct(cdytail, adx, out double cdyt_adx1, out double cdyt_adx0);
                TwoTwoDiff(cdxt_ady1, cdxt_ady0, cdyt_adx1, cdyt_adx0, out double ct_alarge, out double a2, out double a1, out double a0);
                ct_a[0] = a0; ct_a[1] = a1; ct_a[2] = a2; ct_a[3] = ct_alarge; ct_alen = 4;
                TwoProduct(cdytail, bdx, out double cdyt_bdx1, out double cdyt_bdx0);
                TwoProduct(cdxtail, bdy, out double cdxt_bdy1, out double cdxt_bdy0);
                TwoTwoDiff(cdyt_bdx1, cdyt_bdx0, cdxt_bdy1, cdxt_bdy0, out double ct_blarge, out double b2, out double b1, out double b0);
                ct_b[0] = b0; ct_b[1] = b1; ct_b[2] = b2; ct_b[3] = ct_blarge; ct_blen = 4;
            }
        }

        Span<double> bct = stackalloc double[8];
        Span<double> cat = stackalloc double[8];
        Span<double> abt = stackalloc double[8];
        Span<double> u = stackalloc double[4];
        Span<double> v = stackalloc double[12];
        Span<double> w = stackalloc double[16];
        int wlength, vlength;

        int bctlen = FastExpansionSumZeroElim(bt_clen, bt_c, ct_blen, ct_b, bct);
        wlength = ScaleExpansionZeroElim(bctlen, bct, adz, w);
        finlength = FastExpansionSumZeroElim(finlength, finnow, wlength, w, finother);
        Swap(ref finnow, ref finother);

        int catlen = FastExpansionSumZeroElim(ct_alen, ct_a, at_clen, at_c, cat);
        wlength = ScaleExpansionZeroElim(catlen, cat, bdz, w);
        finlength = FastExpansionSumZeroElim(finlength, finnow, wlength, w, finother);
        Swap(ref finnow, ref finother);

        int abtlen = FastExpansionSumZeroElim(at_blen, at_b, bt_alen, bt_a, abt);
        wlength = ScaleExpansionZeroElim(abtlen, abt, cdz, w);
        finlength = FastExpansionSumZeroElim(finlength, finnow, wlength, w, finother);
        Swap(ref finnow, ref finother);

        if (adztail != 0.0)
        {
            vlength = ScaleExpansionZeroElim(4, bc, adztail, v);
            finlength = FastExpansionSumZeroElim(finlength, finnow, vlength, v, finother);
            Swap(ref finnow, ref finother);
        }
        if (bdztail != 0.0)
        {
            vlength = ScaleExpansionZeroElim(4, ca, bdztail, v);
            finlength = FastExpansionSumZeroElim(finlength, finnow, vlength, v, finother);
            Swap(ref finnow, ref finother);
        }
        if (cdztail != 0.0)
        {
            vlength = ScaleExpansionZeroElim(4, ab, cdztail, v);
            finlength = FastExpansionSumZeroElim(finlength, finnow, vlength, v, finother);
            Swap(ref finnow, ref finother);
        }

        if (adxtail != 0.0)
        {
            if (bdytail != 0.0)
            {
                TwoProduct(adxtail, bdytail, out double adxt_bdyt1, out double adxt_bdyt0);
                TwoOneProduct(adxt_bdyt1, adxt_bdyt0, cdz, out double u3, out double u2, out double u1, out double u0);
                u[0] = u0; u[1] = u1; u[2] = u2; u[3] = u3;
                finlength = FastExpansionSumZeroElim(finlength, finnow, 4, u, finother);
                Swap(ref finnow, ref finother);
                if (cdztail != 0.0)
                {
                    TwoOneProduct(adxt_bdyt1, adxt_bdyt0, cdztail, out u3, out u2, out u1, out u0);
                    u[0] = u0; u[1] = u1; u[2] = u2; u[3] = u3;
                    finlength = FastExpansionSumZeroElim(finlength, finnow, 4, u, finother);
                    Swap(ref finnow, ref finother);
                }
            }
            if (cdytail != 0.0)
            {
                negate = -adxtail;
                TwoProduct(negate, cdytail, out double adxt_cdyt1, out double adxt_cdyt0);
                TwoOneProduct(adxt_cdyt1, adxt_cdyt0, bdz, out double u3, out double u2, out double u1, out double u0);
                u[0] = u0; u[1] = u1; u[2] = u2; u[3] = u3;
                finlength = FastExpansionSumZeroElim(finlength, finnow, 4, u, finother);
                Swap(ref finnow, ref finother);
                if (bdztail != 0.0)
                {
                    TwoOneProduct(adxt_cdyt1, adxt_cdyt0, bdztail, out u3, out u2, out u1, out u0);
                    u[0] = u0; u[1] = u1; u[2] = u2; u[3] = u3;
                    finlength = FastExpansionSumZeroElim(finlength, finnow, 4, u, finother);
                    Swap(ref finnow, ref finother);
                }
            }
        }
        if (bdxtail != 0.0)
        {
            if (cdytail != 0.0)
            {
                TwoProduct(bdxtail, cdytail, out double bdxt_cdyt1, out double bdxt_cdyt0);
                TwoOneProduct(bdxt_cdyt1, bdxt_cdyt0, adz, out double u3, out double u2, out double u1, out double u0);
                u[0] = u0; u[1] = u1; u[2] = u2; u[3] = u3;
                finlength = FastExpansionSumZeroElim(finlength, finnow, 4, u, finother);
                Swap(ref finnow, ref finother);
                if (adztail != 0.0)
                {
                    TwoOneProduct(bdxt_cdyt1, bdxt_cdyt0, adztail, out u3, out u2, out u1, out u0);
                    u[0] = u0; u[1] = u1; u[2] = u2; u[3] = u3;
                    finlength = FastExpansionSumZeroElim(finlength, finnow, 4, u, finother);
                    Swap(ref finnow, ref finother);
                }
            }
            if (adytail != 0.0)
            {
                negate = -bdxtail;
                TwoProduct(negate, adytail, out double bdxt_adyt1, out double bdxt_adyt0);
                TwoOneProduct(bdxt_adyt1, bdxt_adyt0, cdz, out double u3, out double u2, out double u1, out double u0);
                u[0] = u0; u[1] = u1; u[2] = u2; u[3] = u3;
                finlength = FastExpansionSumZeroElim(finlength, finnow, 4, u, finother);
                Swap(ref finnow, ref finother);
                if (cdztail != 0.0)
                {
                    TwoOneProduct(bdxt_adyt1, bdxt_adyt0, cdztail, out u3, out u2, out u1, out u0);
                    u[0] = u0; u[1] = u1; u[2] = u2; u[3] = u3;
                    finlength = FastExpansionSumZeroElim(finlength, finnow, 4, u, finother);
                    Swap(ref finnow, ref finother);
                }
            }
        }
        if (cdxtail != 0.0)
        {
            if (adytail != 0.0)
            {
                TwoProduct(cdxtail, adytail, out double cdxt_adyt1, out double cdxt_adyt0);
                TwoOneProduct(cdxt_adyt1, cdxt_adyt0, bdz, out double u3, out double u2, out double u1, out double u0);
                u[0] = u0; u[1] = u1; u[2] = u2; u[3] = u3;
                finlength = FastExpansionSumZeroElim(finlength, finnow, 4, u, finother);
                Swap(ref finnow, ref finother);
                if (bdztail != 0.0)
                {
                    TwoOneProduct(cdxt_adyt1, cdxt_adyt0, bdztail, out u3, out u2, out u1, out u0);
                    u[0] = u0; u[1] = u1; u[2] = u2; u[3] = u3;
                    finlength = FastExpansionSumZeroElim(finlength, finnow, 4, u, finother);
                    Swap(ref finnow, ref finother);
                }
            }
            if (bdytail != 0.0)
            {
                negate = -cdxtail;
                TwoProduct(negate, bdytail, out double cdxt_bdyt1, out double cdxt_bdyt0);
                TwoOneProduct(cdxt_bdyt1, cdxt_bdyt0, adz, out double u3, out double u2, out double u1, out double u0);
                u[0] = u0; u[1] = u1; u[2] = u2; u[3] = u3;
                finlength = FastExpansionSumZeroElim(finlength, finnow, 4, u, finother);
                Swap(ref finnow, ref finother);
                if (adztail != 0.0)
                {
                    TwoOneProduct(cdxt_bdyt1, cdxt_bdyt0, adztail, out u3, out u2, out u1, out u0);
                    u[0] = u0; u[1] = u1; u[2] = u2; u[3] = u3;
                    finlength = FastExpansionSumZeroElim(finlength, finnow, 4, u, finother);
                    Swap(ref finnow, ref finother);
                }
            }
        }

        if (adztail != 0.0)
        {
            wlength = ScaleExpansionZeroElim(bctlen, bct, adztail, w);
            finlength = FastExpansionSumZeroElim(finlength, finnow, wlength, w, finother);
            Swap(ref finnow, ref finother);
        }
        if (bdztail != 0.0)
        {
            wlength = ScaleExpansionZeroElim(catlen, cat, bdztail, w);
            finlength = FastExpansionSumZeroElim(finlength, finnow, wlength, w, finother);
            Swap(ref finnow, ref finother);
        }
        if (cdztail != 0.0)
        {
            wlength = ScaleExpansionZeroElim(abtlen, abt, cdztail, w);
            finlength = FastExpansionSumZeroElim(finlength, finnow, wlength, w, finother);
            Swap(ref finnow, ref finother);
        }

        return finnow[finlength - 1];
    }

    private static void Swap(ref Span<double> a, ref Span<double> b)
    {
        Span<double> t = a; a = b; b = t;
    }

    // ---- insphere ----
    //
    // The adaptive and exact in-sphere stages need large work buffers (the exact determinant reaches
    // 5760 components). InSphereAdapt is hit often on near-cospherical inputs (e.g. probe grids), so we
    // reuse per-thread scratch arrays instead of allocating per call. Tetrahedralization is single
    // threaded today, but [ThreadStatic] keeps the predicate safe if it's ever called concurrently.

    private sealed class InSphereScratch
    {
        // InSphereAdapt
        public readonly double[] ab = new double[4], bc = new double[4], cd = new double[4],
                                 da = new double[4], ac = new double[4], bd = new double[4];
        public readonly double[] t8a = new double[8], t8b = new double[8], t8c = new double[8],
                                 t16 = new double[16], t24 = new double[24], t48 = new double[48];
        public readonly double[] xdet = new double[96], ydet = new double[96], zdet = new double[96], xydet = new double[192];
        public readonly double[] adet = new double[288], bdet = new double[288], cdet = new double[288], ddet = new double[288];
        public readonly double[] abdet = new double[576], cddet = new double[576];
        public readonly double[] fin = new double[1152];

        // InSphereExact (separate buffers; sized for the full exact in-sphere expansion)
        public readonly double[] eab = new double[4], ebc = new double[4], ecd = new double[4],
                                 ede = new double[4], eea = new double[4], eac = new double[4],
                                 ebd = new double[4], ece = new double[4], eda = new double[4], eeb = new double[4];
        public readonly double[] xt8a = new double[8], xt8b = new double[8], xt16 = new double[16];
        public readonly double[] abc = new double[24], bcd = new double[24], cde = new double[24],
                                 dea = new double[24], eabx = new double[24], abd = new double[24],
                                 bce = new double[24], cda = new double[24], deb = new double[24], eacx = new double[24];
        public readonly double[] t48a = new double[48], t48b = new double[48];
        public readonly double[] abcd = new double[96], bcde = new double[96], cdea = new double[96],
                                 deab = new double[96], eabc = new double[96];
        public readonly double[] t192 = new double[192];
        public readonly double[] d384x = new double[384], d384y = new double[384], d384z = new double[384];
        public readonly double[] dxy = new double[768];
        public readonly double[] xadet = new double[1152], xbdet = new double[1152], xcdet = new double[1152],
                                 xddet = new double[1152], xedet = new double[1152];
        public readonly double[] xabdet = new double[2304], xcddet = new double[2304], cdedet = new double[3456];
        public readonly double[] deter = new double[5760];
    }

    [ThreadStatic] private static InSphereScratch? _scratch;
    private static InSphereScratch Scratch => _scratch ??= new InSphereScratch();

    /// <summary>
    /// Returns a positive value if point pe lies inside the sphere through pa,pb,pc,pd; negative if
    /// outside; zero if cospherical. The four sphere points must have positive orientation
    /// (orient3d(pa,pb,pc,pd) &gt; 0) or the sign is reversed. Exact sign.
    /// </summary>
    public static double InSphere(ReadOnlySpan<double> pa, ReadOnlySpan<double> pb, ReadOnlySpan<double> pc, ReadOnlySpan<double> pd, ReadOnlySpan<double> pe)
    {
        double aex = pa[0] - pe[0], bex = pb[0] - pe[0], cex = pc[0] - pe[0], dex = pd[0] - pe[0];
        double aey = pa[1] - pe[1], bey = pb[1] - pe[1], cey = pc[1] - pe[1], dey = pd[1] - pe[1];
        double aez = pa[2] - pe[2], bez = pb[2] - pe[2], cez = pc[2] - pe[2], dez = pd[2] - pe[2];

        double aexbey = aex * bey, bexaey = bex * aey, ab = aexbey - bexaey;
        double bexcey = bex * cey, cexbey = cex * bey, bc = bexcey - cexbey;
        double cexdey = cex * dey, dexcey = dex * cey, cd = cexdey - dexcey;
        double dexaey = dex * aey, aexdey = aex * dey, da = dexaey - aexdey;
        double aexcey = aex * cey, cexaey = cex * aey, ac = aexcey - cexaey;
        double bexdey = bex * dey, dexbey = dex * bey, bd = bexdey - dexbey;

        double abc = aez * bc - bez * ac + cez * ab;
        double bcd = bez * cd - cez * bd + dez * bc;
        double cda = cez * da + dez * ac + aez * cd;
        double dab = dez * ab + aez * bd + bez * da;

        double alift = aex * aex + aey * aey + aez * aez;
        double blift = bex * bex + bey * bey + bez * bez;
        double clift = cex * cex + cey * cey + cez * cez;
        double dlift = dex * dex + dey * dey + dez * dez;

        double det = (dlift * abc - clift * dab) + (blift * cda - alift * bcd);

        double aezplus = Math.Abs(aez), bezplus = Math.Abs(bez), cezplus = Math.Abs(cez), dezplus = Math.Abs(dez);
        double aexbeyplus = Math.Abs(aexbey), bexaeyplus = Math.Abs(bexaey);
        double bexceyplus = Math.Abs(bexcey), cexbeyplus = Math.Abs(cexbey);
        double cexdeyplus = Math.Abs(cexdey), dexceyplus = Math.Abs(dexcey);
        double dexaeyplus = Math.Abs(dexaey), aexdeyplus = Math.Abs(aexdey);
        double aexceyplus = Math.Abs(aexcey), cexaeyplus = Math.Abs(cexaey);
        double bexdeyplus = Math.Abs(bexdey), dexbeyplus = Math.Abs(dexbey);
        double permanent = ((cexdeyplus + dexceyplus) * bezplus + (dexbeyplus + bexdeyplus) * cezplus + (bexceyplus + cexbeyplus) * dezplus) * alift
                         + ((dexaeyplus + aexdeyplus) * cezplus + (aexceyplus + cexaeyplus) * dezplus + (cexdeyplus + dexceyplus) * aezplus) * blift
                         + ((aexbeyplus + bexaeyplus) * dezplus + (bexdeyplus + dexbeyplus) * aezplus + (dexaeyplus + aexdeyplus) * bezplus) * clift
                         + ((bexceyplus + cexbeyplus) * aezplus + (cexaeyplus + aexceyplus) * bezplus + (aexbeyplus + bexaeyplus) * cezplus) * dlift;
        double errbound = isperrboundA * permanent;
        if ((det > errbound) || (-det > errbound))
            return det;

        return InSphereAdapt(pa, pb, pc, pd, pe, permanent);
    }

    private static double InSphereAdapt(ReadOnlySpan<double> pa, ReadOnlySpan<double> pb, ReadOnlySpan<double> pc, ReadOnlySpan<double> pd, ReadOnlySpan<double> pe, double permanent)
    {
        var s = Scratch;
        double[] ab = s.ab, bc = s.bc, cd = s.cd, da = s.da, ac = s.ac, bd = s.bd;
        double[] t8a = s.t8a, t8b = s.t8b, t8c = s.t8c, t16 = s.t16, t24 = s.t24, t48 = s.t48;
        double[] xdet = s.xdet, ydet = s.ydet, zdet = s.zdet, xydet = s.xydet;
        double[] adet = s.adet, bdet = s.bdet, cdet = s.cdet, ddet = s.ddet;
        double[] abdet = s.abdet, cddet = s.cddet, fin1 = s.fin;

        double aex = pa[0] - pe[0], bex = pb[0] - pe[0], cex = pc[0] - pe[0], dex = pd[0] - pe[0];
        double aey = pa[1] - pe[1], bey = pb[1] - pe[1], cey = pc[1] - pe[1], dey = pd[1] - pe[1];
        double aez = pa[2] - pe[2], bez = pb[2] - pe[2], cez = pc[2] - pe[2], dez = pd[2] - pe[2];

        TwoProduct(aex, bey, out double aexbey1, out double aexbey0);
        TwoProduct(bex, aey, out double bexaey1, out double bexaey0);
        TwoTwoDiff(aexbey1, aexbey0, bexaey1, bexaey0, out ab[3], out ab[2], out ab[1], out ab[0]);
        TwoProduct(bex, cey, out double bexcey1, out double bexcey0);
        TwoProduct(cex, bey, out double cexbey1, out double cexbey0);
        TwoTwoDiff(bexcey1, bexcey0, cexbey1, cexbey0, out bc[3], out bc[2], out bc[1], out bc[0]);
        TwoProduct(cex, dey, out double cexdey1, out double cexdey0);
        TwoProduct(dex, cey, out double dexcey1, out double dexcey0);
        TwoTwoDiff(cexdey1, cexdey0, dexcey1, dexcey0, out cd[3], out cd[2], out cd[1], out cd[0]);
        TwoProduct(dex, aey, out double dexaey1, out double dexaey0);
        TwoProduct(aex, dey, out double aexdey1, out double aexdey0);
        TwoTwoDiff(dexaey1, dexaey0, aexdey1, aexdey0, out da[3], out da[2], out da[1], out da[0]);
        TwoProduct(aex, cey, out double aexcey1, out double aexcey0);
        TwoProduct(cex, aey, out double cexaey1, out double cexaey0);
        TwoTwoDiff(aexcey1, aexcey0, cexaey1, cexaey0, out ac[3], out ac[2], out ac[1], out ac[0]);
        TwoProduct(bex, dey, out double bexdey1, out double bexdey0);
        TwoProduct(dex, bey, out double dexbey1, out double dexbey0);
        TwoTwoDiff(bexdey1, bexdey0, dexbey1, dexbey0, out bd[3], out bd[2], out bd[1], out bd[0]);

        int alen = AdaptRow(cd, bd, bc, bez, -cez, dez, aex, aey, aez,
            t8a, t8b, t8c, t16, t24, t48, xdet, ydet, zdet, xydet, adet, negX: true);
        int blen = AdaptRow(da, ac, cd, cez, dez, aez, bex, bey, bez,
            t8a, t8b, t8c, t16, t24, t48, xdet, ydet, zdet, xydet, bdet, negX: false);
        int clen = AdaptRow(ab, bd, da, dez, aez, bez, cex, cey, cez,
            t8a, t8b, t8c, t16, t24, t48, xdet, ydet, zdet, xydet, cdet, negX: true);
        int dlen = AdaptRow(bc, ac, ab, aez, -bez, cez, dex, dey, dez,
            t8a, t8b, t8c, t16, t24, t48, xdet, ydet, zdet, xydet, ddet, negX: false);

        int ablen = FastExpansionSumZeroElim(alen, adet, blen, bdet, abdet);
        int cdlen = FastExpansionSumZeroElim(clen, cdet, dlen, ddet, cddet);
        int finlength = FastExpansionSumZeroElim(ablen, abdet, cdlen, cddet, fin1);

        double det = Estimate(finlength, fin1);
        double errbound = isperrboundB * permanent;
        if ((det >= errbound) || (-det >= errbound))
            return det;

        double aextail = TwoDiffTail(pa[0], pe[0], aex), aeytail = TwoDiffTail(pa[1], pe[1], aey), aeztail = TwoDiffTail(pa[2], pe[2], aez);
        double bextail = TwoDiffTail(pb[0], pe[0], bex), beytail = TwoDiffTail(pb[1], pe[1], bey), beztail = TwoDiffTail(pb[2], pe[2], bez);
        double cextail = TwoDiffTail(pc[0], pe[0], cex), ceytail = TwoDiffTail(pc[1], pe[1], cey), ceztail = TwoDiffTail(pc[2], pe[2], cez);
        double dextail = TwoDiffTail(pd[0], pe[0], dex), deytail = TwoDiffTail(pd[1], pe[1], dey), deztail = TwoDiffTail(pd[2], pe[2], dez);
        if (aextail == 0.0 && aeytail == 0.0 && aeztail == 0.0 && bextail == 0.0 && beytail == 0.0 && beztail == 0.0
            && cextail == 0.0 && ceytail == 0.0 && ceztail == 0.0 && dextail == 0.0 && deytail == 0.0 && deztail == 0.0)
            return det;

        errbound = isperrboundC * permanent + resulterrbound * Math.Abs(det);
        double ab3 = ab[3], bc3 = bc[3], cd3 = cd[3], da3 = da[3], ac3 = ac[3], bd3 = bd[3];
        double abeps = (aex * beytail + bey * aextail) - (aey * bextail + bex * aeytail);
        double bceps = (bex * ceytail + cey * bextail) - (bey * cextail + cex * beytail);
        double cdeps = (cex * deytail + dey * cextail) - (cey * dextail + dex * ceytail);
        double daeps = (dex * aeytail + aey * dextail) - (dey * aextail + aex * deytail);
        double aceps = (aex * ceytail + cey * aextail) - (aey * cextail + cex * aeytail);
        double bdeps = (bex * deytail + dey * bextail) - (bey * dextail + dex * beytail);
        det += (((bex * bex + bey * bey + bez * bez) * ((cez * daeps + dez * aceps + aez * cdeps) + (ceztail * da3 + deztail * ac3 + aeztail * cd3))
                 + (dex * dex + dey * dey + dez * dez) * ((aez * bceps - bez * aceps + cez * abeps) + (aeztail * bc3 - beztail * ac3 + ceztail * ab3)))
                - ((aex * aex + aey * aey + aez * aez) * ((bez * cdeps - cez * bdeps + dez * bceps) + (beztail * cd3 - ceztail * bd3 + deztail * bc3))
                 + (cex * cex + cey * cey + cez * cez) * ((dez * abeps + aez * bdeps + bez * daeps) + (deztail * ab3 + aeztail * bd3 + beztail * da3))))
             + 2.0 * (((bex * bextail + bey * beytail + bez * beztail) * (cez * da3 + dez * ac3 + aez * cd3)
                       + (dex * dextail + dey * deytail + dez * deztail) * (aez * bc3 - bez * ac3 + cez * ab3))
                      - ((aex * aextail + aey * aeytail + aez * aeztail) * (bez * cd3 - cez * bd3 + dez * bc3)
                       + (cex * cextail + cey * ceytail + cez * ceztail) * (dez * ab3 + aez * bd3 + bez * da3)));
        if ((det >= errbound) || (-det >= errbound))
            return det;

        return InSphereExact(pa, pb, pc, pd, pe);
    }

    // One of the four 3x3 "lift" rows of insphereadapt: temp24 = e0*c1 + e1*c2 + e2*c3 (the cofactor
    // expansion), then squared-distance lift (x^2+y^2+z^2) scaled, summed into 'outDet'. 'negX' negates
    // the x and y squared terms (matching the alternating signs of rows a and c in the determinant).
    private static int AdaptRow(double[] e0, double[] e1, double[] e2, double s0, double s1, double s2,
                                double lx, double ly, double lz,
                                double[] t8a, double[] t8b, double[] t8c, double[] t16, double[] t24, double[] t48,
                                double[] xdet, double[] ydet, double[] zdet, double[] xydet, double[] outDet, bool negX)
    {
        int t8alen = ScaleExpansionZeroElim(4, e0, s0, t8a);
        int t8blen = ScaleExpansionZeroElim(4, e1, s1, t8b);
        int t8clen = ScaleExpansionZeroElim(4, e2, s2, t8c);
        int t16len = FastExpansionSumZeroElim(t8alen, t8a, t8blen, t8b, t16);
        int t24len = FastExpansionSumZeroElim(t8clen, t8c, t16len, t16, t24);
        int t48len = ScaleExpansionZeroElim(t24len, t24, lx, t48);
        int xlen = ScaleExpansionZeroElim(t48len, t48, negX ? -lx : lx, xdet);
        t48len = ScaleExpansionZeroElim(t24len, t24, ly, t48);
        int ylen = ScaleExpansionZeroElim(t48len, t48, negX ? -ly : ly, ydet);
        t48len = ScaleExpansionZeroElim(t24len, t24, lz, t48);
        int zlen = ScaleExpansionZeroElim(t48len, t48, negX ? -lz : lz, zdet);
        int xylen = FastExpansionSumZeroElim(xlen, xdet, ylen, ydet, xydet);
        return FastExpansionSumZeroElim(xylen, xydet, zlen, zdet, outDet);
    }

    private static double InSphereExact(ReadOnlySpan<double> pa, ReadOnlySpan<double> pb, ReadOnlySpan<double> pc, ReadOnlySpan<double> pd, ReadOnlySpan<double> pe)
    {
        var s = Scratch;
        double[] ab = s.eab, bc = s.ebc, cd = s.ecd, de = s.ede, ea = s.eea, ac = s.eac, bd = s.ebd, ce = s.ece, da = s.eda, eb = s.eeb;
        double[] t8a = s.xt8a, t8b = s.xt8b, t16 = s.xt16;
        double[] abc = s.abc, bcd = s.bcd, cde = s.cde, dea = s.dea, eab = s.eabx, abd = s.abd, bce = s.bce, cda = s.cda, deb = s.deb, eac = s.eacx;
        double[] t48a = s.t48a, t48b = s.t48b;
        double[] abcd = s.abcd, bcde = s.bcde, cdea = s.cdea, deab = s.deab, eabc = s.eabc;
        double[] t192 = s.t192, d384x = s.d384x, d384y = s.d384y, d384z = s.d384z, dxy = s.dxy;
        double[] adet = s.xadet, bdet = s.xbdet, cdet = s.xcdet, ddet = s.xddet, edet = s.xedet;
        double[] abdet = s.xabdet, cddet = s.xcddet, cdedet = s.cdedet, deter = s.deter;

        TwoProduct(pa[0], pb[1], out double axby1, out double axby0);
        TwoProduct(pb[0], pa[1], out double bxay1, out double bxay0);
        TwoTwoDiff(axby1, axby0, bxay1, bxay0, out ab[3], out ab[2], out ab[1], out ab[0]);
        TwoProduct(pb[0], pc[1], out double bxcy1, out double bxcy0);
        TwoProduct(pc[0], pb[1], out double cxby1, out double cxby0);
        TwoTwoDiff(bxcy1, bxcy0, cxby1, cxby0, out bc[3], out bc[2], out bc[1], out bc[0]);
        TwoProduct(pc[0], pd[1], out double cxdy1, out double cxdy0);
        TwoProduct(pd[0], pc[1], out double dxcy1, out double dxcy0);
        TwoTwoDiff(cxdy1, cxdy0, dxcy1, dxcy0, out cd[3], out cd[2], out cd[1], out cd[0]);
        TwoProduct(pd[0], pe[1], out double dxey1, out double dxey0);
        TwoProduct(pe[0], pd[1], out double exdy1, out double exdy0);
        TwoTwoDiff(dxey1, dxey0, exdy1, exdy0, out de[3], out de[2], out de[1], out de[0]);
        TwoProduct(pe[0], pa[1], out double exay1, out double exay0);
        TwoProduct(pa[0], pe[1], out double axey1, out double axey0);
        TwoTwoDiff(exay1, exay0, axey1, axey0, out ea[3], out ea[2], out ea[1], out ea[0]);
        TwoProduct(pa[0], pc[1], out double axcy1, out double axcy0);
        TwoProduct(pc[0], pa[1], out double cxay1, out double cxay0);
        TwoTwoDiff(axcy1, axcy0, cxay1, cxay0, out ac[3], out ac[2], out ac[1], out ac[0]);
        TwoProduct(pb[0], pd[1], out double bxdy1, out double bxdy0);
        TwoProduct(pd[0], pb[1], out double dxby1, out double dxby0);
        TwoTwoDiff(bxdy1, bxdy0, dxby1, dxby0, out bd[3], out bd[2], out bd[1], out bd[0]);
        TwoProduct(pc[0], pe[1], out double cxey1, out double cxey0);
        TwoProduct(pe[0], pc[1], out double excy1, out double excy0);
        TwoTwoDiff(cxey1, cxey0, excy1, excy0, out ce[3], out ce[2], out ce[1], out ce[0]);
        TwoProduct(pd[0], pa[1], out double dxay1, out double dxay0);
        TwoProduct(pa[0], pd[1], out double axdy1, out double axdy0);
        TwoTwoDiff(dxay1, dxay0, axdy1, axdy0, out da[3], out da[2], out da[1], out da[0]);
        TwoProduct(pe[0], pb[1], out double exby1, out double exby0);
        TwoProduct(pb[0], pe[1], out double bxey1, out double bxey0);
        TwoTwoDiff(exby1, exby0, bxey1, bxey0, out eb[3], out eb[2], out eb[1], out eb[0]);

        int abclen = Tri(bc, ac, ab, pa[2], -pb[2], pc[2], t8a, t8b, t16, abc);
        int bcdlen = Tri(cd, bd, bc, pb[2], -pc[2], pd[2], t8a, t8b, t16, bcd);
        int cdelen = Tri(de, ce, cd, pc[2], -pd[2], pe[2], t8a, t8b, t16, cde);
        int dealen = Tri(ea, da, de, pd[2], -pe[2], pa[2], t8a, t8b, t16, dea);
        int eablen = Tri(ab, eb, ea, pe[2], -pa[2], pb[2], t8a, t8b, t16, eab);
        int abdlen = Tri(bd, da, ab, pa[2], pb[2], pd[2], t8a, t8b, t16, abd);
        int bcelen = Tri(ce, eb, bc, pb[2], pc[2], pe[2], t8a, t8b, t16, bce);
        int cdalen = Tri(da, ac, cd, pc[2], pd[2], pa[2], t8a, t8b, t16, cda);
        int deblen = Tri(eb, bd, de, pd[2], pe[2], pb[2], t8a, t8b, t16, deb);
        int eaclen = Tri(ac, ce, ea, pe[2], pa[2], pc[2], t8a, t8b, t16, eac);

        int alen = Vert(cdelen, cde, bcelen, bce, deblen, deb, bcdlen, bcd, pa, t48a, t48b, bcde, t192, d384x, d384y, d384z, dxy, adet);
        int blen = Vert(dealen, dea, cdalen, cda, eaclen, eac, cdelen, cde, pb, t48a, t48b, cdea, t192, d384x, d384y, d384z, dxy, bdet);
        int clen = Vert(eablen, eab, deblen, deb, abdlen, abd, dealen, dea, pc, t48a, t48b, deab, t192, d384x, d384y, d384z, dxy, cdet);
        int dlen = Vert(abclen, abc, eaclen, eac, bcelen, bce, eablen, eab, pd, t48a, t48b, eabc, t192, d384x, d384y, d384z, dxy, ddet);
        int elen = Vert(bcdlen, bcd, abdlen, abd, cdalen, cda, abclen, abc, pe, t48a, t48b, abcd, t192, d384x, d384y, d384z, dxy, edet);

        int ablen = FastExpansionSumZeroElim(alen, adet, blen, bdet, abdet);
        int cdlen = FastExpansionSumZeroElim(clen, cdet, dlen, ddet, cddet);
        int cdelen2 = FastExpansionSumZeroElim(cdlen, cddet, elen, edet, cdedet);
        int deterlen = FastExpansionSumZeroElim(ablen, abdet, cdelen2, cdedet, deter);

        return deter[deterlen - 1];
    }

    // One of insphereexact's ten triple products: out = e0*z0 + e1*z1 + e2*z2 (each ei a length-4
    // expansion, zi a plain coordinate), accumulated as scale + fast-sum. Returns the result length.
    private static int Tri(double[] e0, double[] e1, double[] e2, double z0, double z1, double z2,
                           double[] t8a, double[] t8b, double[] t16, double[] outE)
    {
        int t8alen = ScaleExpansionZeroElim(4, e0, z0, t8a);
        int t8blen = ScaleExpansionZeroElim(4, e1, z1, t8b);
        int t16len = FastExpansionSumZeroElim(t8alen, t8a, t8blen, t8b, t16);
        t8alen = ScaleExpansionZeroElim(4, e2, z2, t8a);
        return FastExpansionSumZeroElim(t8alen, t8a, t16len, t16, outE);
    }

    // One of insphereexact's five vertex contributions: combine four triple-products into the signed
    // face volume 'face' = (p0 + p1) - (n0 + n1), lift it by the vertex's squared distance
    // (x^2 + y^2 + z^2 via double scaling by the coordinate), and return the length written to outDet.
    private static int Vert(int p0len, double[] p0, int p1len, double[] p1, int n0len, double[] n0, int n1len, double[] n1,
                            ReadOnlySpan<double> pv, double[] t48a, double[] t48b, double[] face, double[] t192,
                            double[] d384x, double[] d384y, double[] d384z, double[] dxy, double[] outDet)
    {
        int t48alen = FastExpansionSumZeroElim(p0len, p0, p1len, p1, t48a);
        int t48blen = FastExpansionSumZeroElim(n0len, n0, n1len, n1, t48b);
        for (int i = 0; i < t48blen; i++) t48b[i] = -t48b[i];
        int facelen = FastExpansionSumZeroElim(t48alen, t48a, t48blen, t48b, face);

        int xlen = ScaleExpansionZeroElim(facelen, face, pv[0], t192);
        xlen = ScaleExpansionZeroElim(xlen, t192, pv[0], d384x);
        int ylen = ScaleExpansionZeroElim(facelen, face, pv[1], t192);
        ylen = ScaleExpansionZeroElim(ylen, t192, pv[1], d384y);
        int zlen = ScaleExpansionZeroElim(facelen, face, pv[2], t192);
        zlen = ScaleExpansionZeroElim(zlen, t192, pv[2], d384z);
        int xylen = FastExpansionSumZeroElim(xlen, d384x, ylen, d384y, dxy);
        return FastExpansionSumZeroElim(xylen, dxy, zlen, d384z, outDet);
    }
}
