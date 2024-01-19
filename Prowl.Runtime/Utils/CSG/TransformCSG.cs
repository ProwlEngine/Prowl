namespace Prowl.Runtime.CSG
{
    internal class TransformCSG
    {
        private System.Numerics.Vector3 Basisl1 = new System.Numerics.Vector3(1, 0, 0);
        private System.Numerics.Vector3 Basisl2 = new System.Numerics.Vector3(0, 1, 0);
        private System.Numerics.Vector3 Basisl3 = new System.Numerics.Vector3(0, 0, 1);
        private System.Numerics.Vector3 position = new System.Numerics.Vector3(0, 0, 0);

        public TransformCSG() { }

        public TransformCSG(System.Numerics.Vector3 pos, System.Numerics.Vector3 basisl1, System.Numerics.Vector3 basisl2, System.Numerics.Vector3 basisl3)
        {
            this.Basisl1 = basisl1;
            this.Basisl2 = basisl2;
            this.Basisl3 = basisl3;
            this.position = pos;
        }

        public void BasisSetColumn(int col, System.Numerics.Vector3 valeur)
        {
            this.Basisl1[col] = valeur[0];
            this.Basisl2[col] = valeur[1];
            this.Basisl3[col] = valeur[2];
        }

        public System.Numerics.Vector3 BasisGetColumn(int col) => new(Basisl1[col], Basisl2[col], Basisl3[col]);

        public System.Numerics.Vector3 XForm(Vector3 vector) => new(System.Numerics.Vector3.Dot(Basisl1, vector) + position.X,
                                                    System.Numerics.Vector3.Dot(Basisl2, vector) + position.Y,
                                                    System.Numerics.Vector3.Dot(Basisl3, vector) + position.Z);

        System.Numerics.Vector3 BasisXForm(System.Numerics.Vector3 vector) => new(System.Numerics.Vector3.Dot(Basisl1, vector),
                                                          System.Numerics.Vector3.Dot(Basisl2, vector),
                                                          System.Numerics.Vector3.Dot(Basisl3, vector));

        void AffineInvert()
        {
            this.Basis_invert();
            this.position = this.BasisXForm(-position);
        }

        void Basis_invert()
        {
            float[] co = {
                Cofac(ref Basisl2, 1, ref Basisl3, 2), Cofac(ref Basisl2, 2, ref Basisl3, 0), Cofac(ref Basisl2, 0, ref Basisl3, 1)
            };
            float det = (float)Basisl1[0] * co[0] + (float)Basisl1[1] * co[1] + (float)Basisl1[2] * co[2];

            float s = 1.0f / det;

            this.SetBasis(co[0] * s, Cofac(ref Basisl1, 2, ref Basisl3, 1) * s, Cofac(ref Basisl1, 1, ref Basisl2, 2) * s,
                    co[1] * s, Cofac(ref Basisl1, 0, ref Basisl3, 2) * s, Cofac(ref Basisl1, 2, ref Basisl2, 0) * s,
                    co[2] * s, Cofac(ref Basisl1, 1, ref Basisl3, 0) * s, Cofac(ref Basisl1, 0, ref Basisl2, 1) * s);
        }

        float Cofac(ref System.Numerics.Vector3 row1, int col1, ref System.Numerics.Vector3 row2, int col2) => (float)row1[col1] * (float)row2[col2] - (float)row1[col2] * (float)row2[col1];

        void SetBasis(float xx, float xy, float xz, float yx, float yy, float yz, float zx, float zy, float zz)
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

        public void SetPosition(System.Numerics.Vector3 pos) => this.position = new(pos.X, pos.Y, pos.Z);

        public TransformCSG AffineInverse()
        {
            TransformCSG res = new(this.position, Basisl1, Basisl2, Basisl3);
            res.AffineInvert();
            return res;
        }
    }
}