using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp
{
	public class CharStringContext
	{
		public int bounds;
		public float first_x;
		public float first_y;
		public int max_x;
		public int max_y;
		public int min_x;
		public int min_y;
		public int num_vertices;
		public stbtt_vertex[] pvertices;
		public int started;
		public float x;
		public float y;

		public void stbtt__track_vertex(int x, int y)
		{
			if (x > max_x || started == 0)
				max_x = x;
			if (y > max_y || started == 0)
				max_y = y;
			if (x < min_x || started == 0)
				min_x = x;
			if (y < min_y || started == 0)
				min_y = y;
			started = 1;
		}

		public void stbtt__csctx_v(byte type, int x, int y, int cx, int cy, int cx1, int cy1)
		{
			if (bounds != 0)
			{
				stbtt__track_vertex(x, y);
				if (type == STBTT_vcubic)
				{
					stbtt__track_vertex(cx, cy);
					stbtt__track_vertex(cx1, cy1);
				}
			}
			else
			{
				var v = new stbtt_vertex();
				stbtt_setvertex(ref v, type, x, y, cx, cy);
				pvertices[num_vertices] = v;
				pvertices[num_vertices].cx1 = (short)cx1;
				pvertices[num_vertices].cy1 = (short)cy1;
			}

			num_vertices++;
		}

		public void stbtt__csctx_close_shape()
		{
			if (first_x != x || first_y != y)
				stbtt__csctx_v(STBTT_vline, (int)first_x, (int)first_y, 0, 0, 0, 0);
		}

		public void stbtt__csctx_rmove_to(float dx, float dy)
		{
			stbtt__csctx_close_shape();
			first_x = x = x + dx;
			first_y = y = y + dy;
			stbtt__csctx_v(STBTT_vmove, (int)x, (int)y, 0, 0, 0, 0);
		}

		public void stbtt__csctx_rline_to(float dx, float dy)
		{
			x += dx;
			y += dy;
			stbtt__csctx_v(STBTT_vline, (int)x, (int)y, 0, 0, 0, 0);
		}

		public void stbtt__csctx_rccurve_to(float dx1, float dy1, float dx2, float dy2,
			float dx3, float dy3)
		{
			var cx1 = x + dx1;
			var cy1 = y + dy1;
			var cx2 = cx1 + dx2;
			var cy2 = cy1 + dy2;
			x = cx2 + dx3;
			y = cy2 + dy3;
			stbtt__csctx_v(STBTT_vcubic, (int)x, (int)y, (int)cx1, (int)cy1, (int)cx2, (int)cy2);
		}
	}
}