namespace Prowl.Runtime.CSG
{

    // TODO Replace with Runtime.Bounds
    public class AABBCSG
    {
        public double width, height, depth;
        public double x, y, z;

        public AABBCSG()
        {
            x = y = z = 0;
            width = height = depth = 0;
        }

        public AABBCSG(Vector3 pos, Vector3 size)
        {
            this.x = pos.x;
            this.y = pos.y;
            this.z = pos.z;
            this.width = size.x;
            this.height = size.y;
            this.depth = size.z;
        }

        public void Encapsulate(Vector3 point)
        {
            double endx = x + width;
            double endy = y + height;
            double endz = z + depth;

            if (point.x < this.x) this.x = point.x;
            if (point.y < this.y) this.y = point.y;
            if (point.z < this.z) this.z = point.z;

            if (point.x > endx) endx = point.x;
            if (point.y > endy) endy = point.y;
            if (point.z > endz) endz = point.z;

            this.width = endx - this.x;
            this.height = endy - this.y;
            this.depth = endz - this.z;
        }

        public bool IntersectInclusive(AABBCSG aabb) 
            => !((x > (aabb.x + aabb.width)) || ((x + width) < aabb.x) || (y > (aabb.y + aabb.height)) || ((y + height) < aabb.y) || (z > (aabb.z + aabb.depth)) || ((z + depth) < aabb.z));

        public Vector3 GetCenter() => new Vector3(x + (width * 0.5f), y + (height * 0.5f), z + (depth * 0.5f));

        public Vector3 GetPosition() => new Vector3(x, y, z);

        public void SetPosition(Vector3 position)
        {
            this.x = position.x;
            this.y = position.y;
            this.z = position.z;
        }

        public Vector3 GetSize() => new Vector3(width, height, depth);

        public void ExpandBy(double size_grow)
        {
            this.x -= size_grow;
            this.y -= size_grow;
            this.z -= size_grow;
            this.width += 2.0f * size_grow;
            this.height += 2.0f * size_grow;
            this.depth += 2.0f * size_grow;
        }

        public void MergeWith(AABBCSG aabb)
        {
            double minx;
            double miny;
            double minz;

            minx = (x < aabb.x) ? x : aabb.x;
            miny = (y < aabb.y) ? y : aabb.y;
            minz = (z < aabb.z) ? z : aabb.z;

            this.width = (width + x > aabb.width + aabb.x) ? (width + x) - minx : (aabb.width + aabb.x) - minx;
            this.height = (height + y > aabb.height + aabb.y) ? (height + y) - miny : (aabb.height + aabb.y) - miny;
            this.depth = (depth + z > aabb.depth + aabb.z) ? (depth + z) - minz : (aabb.depth + aabb.z) - minz;

            this.x = minx;
            this.y = miny;
            this.z = minz;
        }

        public AABBCSG Copy()
        {
            return new AABBCSG(new Vector3(x, y, z), new Vector3(width, height, depth));
        }

        public AABBCSG ComputeIntersection(AABBCSG aabb)
        {
            double src_maxx = x + width;
            double src_maxy = y + height;
            double src_maxz = z + depth;
            double dst_minx = aabb.x;
            double dst_miny = aabb.y;
            double dst_minz = aabb.z;
            double dst_maxx = aabb.x + aabb.width;
            double dst_maxy = aabb.y + aabb.height;
            double dst_maxz = aabb.z + aabb.depth;

            if ((x > dst_maxx || src_maxx < dst_minx) || (y > dst_maxy || src_maxy < dst_miny) || (z > dst_maxz || src_maxz < dst_minz))
                return new AABBCSG();

            Vector3 min, max;
            min.x = (x > dst_minx) ? x : dst_minx;
            max.x = (src_maxx < dst_maxx) ? src_maxx : dst_maxx;

            min.y = (y > dst_miny) ? y : dst_miny;
            max.y = (src_maxy < dst_maxy) ? src_maxy : dst_maxy;

            min.z = (z > dst_minz) ? z : dst_minz;
            max.z = (src_maxz < dst_maxz) ? src_maxz : dst_maxz;

            return new AABBCSG(min, max - min);
        }

        public int GetLongestAxisIndex()
        {
            int axis = 0;
            double max_size = width;

            if (height > max_size)
            {
                axis = 1;
                max_size = height;
            }

            if (depth > max_size)
                axis = 2;

            return axis;
        }

        public bool IntersectsRay(Vector3 from, Vector3 dir)
        {
            Vector3 c1 = Vector3.zero;
            Vector3 c2 = Vector3.zero;
            Vector3 position = new Vector3(x, y, z);
            Vector3 size = new Vector3(width, height, depth);
            Vector3 end = position + size;
            double near = -1e20f;
            double far = 1e20f;
            int axis;

            for (int i = 0; i < 3; i++)
            {
                if (dir[i] == 0)
                {
                    if ((from[i] < position[i]) || (from[i] > end[i]))
                    {
                        return false;
                    }
                }
                else
                {
                    c1[i] = (position[i] - from[i]) / dir[i];
                    c2[i] = (end[i] - from[i]) / dir[i];

                    if (c1[i] > c2[i])
                    {
                        Vector3 temp = c1;
                        c1 = c2;
                        c2 = temp;
                    }
                    if (c1[i] > near)
                    {
                        near = c1[i];
                        axis = i;
                    }
                    if (c2[i] < far)
                    {
                        far = c2[i];
                    }
                    if ((near > far) || (far < 0))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}