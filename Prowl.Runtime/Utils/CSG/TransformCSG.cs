namespace Prowl.Runtime.CSG
{
    internal class TransformCSG
    {
        private Vector3 Basisl1 = new Vector3(1, 0, 0);
        private Vector3 Basisl2 = new Vector3(0, 1, 0);
        private Vector3 Basisl3 = new Vector3(0, 0, 1);

        private Vector3 position = new Vector3(0, 0, 0);

        internal TransformCSG() {}

        internal TransformCSG(Vector3 pos, Vector3 basisl1, Vector3 basisl2, Vector3 basisl3)
        {
            this.Basisl1 = basisl1;
            this.Basisl2 = basisl2;
            this.Basisl3 = basisl3;
            this.position = pos;
        }

        internal void BasisSetColumn(int col, Vector3 valeur)
        {
            this.Basisl1[col] = valeur[0];
            this.Basisl2[col] = valeur[1];
            this.Basisl3[col] = valeur[2];
        }

        internal Vector3 BasisGetColumn(int col) => new Vector3(Basisl1[col], Basisl2[col], Basisl3[col]);

        internal Vector3 XForm(Vector3 vector) => new Vector3(Vector3.Dot(Basisl1, vector) + position.x, Vector3.Dot(Basisl2, vector) + position.y, Vector3.Dot(Basisl3, vector) + position.z);

        internal void SetPosition(Vector3 pos) => this.position = new(pos.x, pos.y, pos.z);

        internal TransformCSG AffineInverse()
        {
            TransformCSG res = new TransformCSG(this.position, Basisl1, Basisl2, Basisl3);
            res.AffineInvert();
            return res;
        }

        private Vector3 BasisXForm(Vector3 vector) => new Vector3(Vector3.Dot(Basisl1, vector), Vector3.Dot(Basisl2, vector), Vector3.Dot(Basisl3, vector));

        private void AffineInvert()
        {
            this.BasisInvert();
            this.position = this.BasisXForm(-position);
        }

        private void BasisInvert()
        {
            double[] co = {
            Cofac(ref Basisl2, 1, ref Basisl3, 2), Cofac(ref Basisl2, 2, ref Basisl3, 0), Cofac(ref Basisl2, 0, ref Basisl3, 1)
        };
            double det = Basisl1[0] * co[0] + Basisl1[1] * co[1] + Basisl1[2] * co[2];

            double s = 1.0f / det;

            this.SetBasis(co[0] * s, Cofac(ref Basisl1, 2, ref Basisl3, 1) * s, Cofac(ref Basisl1, 1, ref Basisl2, 2) * s,
                    co[1] * s, Cofac(ref Basisl1, 0, ref Basisl3, 2) * s, Cofac(ref Basisl1, 2, ref Basisl2, 0) * s,
                    co[2] * s, Cofac(ref Basisl1, 1, ref Basisl3, 0) * s, Cofac(ref Basisl1, 0, ref Basisl2, 1) * s);
        }

        private double Cofac(ref Vector3 row1, int col1, ref Vector3 row2, int col2) => row1[col1] * row2[col2] - row1[col2] * row2[col1];

        private void SetBasis(double xx, double xy, double xz, double yx, double yy, double yz, double zx, double zy, double zz)
        {
            this.Basisl1[0] = xx;
            this.Basisl1[1] = xy;
            this.Basisl1[2] = xz;
            this.Basisl2[0] = yx;
            this.Basisl2[1] = yy;
            this.Basisl2[2] = yz;
            this.Basisl3[0] = zx;
            this.Basisl3[1] = zy;
            this.Basisl3[2] = zz;
        }
    }
}