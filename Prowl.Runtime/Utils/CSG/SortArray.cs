using System.Collections;
using System.Collections.Generic;

namespace Prowl.Runtime.CSG
{
	internal class SortArray
	{
		private IComparer compare;
		public SortArray(IComparer comp)
		{
			this.compare = comp;
		}

		private int bitlog(int n)
		{
			int k;
			for (k = 0; n != 1; n >>= 1)
			{
				++k;
			}
			return k;
		}

		public void nthElement(int first, int last, int nth, ref (int i, MeshMerge.FaceBVH f)[] array)
		{
			if (first == last || nth == last)
			{
				return;
			}
			introselect(first, nth, last, ref array, bitlog(last - first) * 2);
		}

		private void introselect(int first, int nth, int last, ref (int i, MeshMerge.FaceBVH f)[] array, int max_depth)
		{
			while (last - first > 3)
			{
				if (max_depth == 0)
				{
					partial_select(first, nth + 1, last, ref array);
					(int i, MeshMerge.FaceBVH f) temps = array[first];
					array[first] = array[nth];
					array[nth] = temps;
					return;
				}

				max_depth--;

				int cut = partitioner(
						first,
						last,
						median_of_3(
								array[first],
								array[first + (last - first) / 2],
								array[last - 1]),
						ref array);

				if (cut <= nth)
				{
					first = cut;
				}
				else
				{
					last = cut;
				}
			}
			insertion_sort(first, last, ref array);
		}

		private void insertion_sort(int first, int last, ref (int i, MeshMerge.FaceBVH f)[] array)
		{
			if (first == last)
			{
				return;
			}
			for (int i = first + 1; i != last; i++)
			{
				linear_insert(first, i, ref array);
			}
		}

		private void linear_insert(int first, int last, ref (int i, MeshMerge.FaceBVH f)[] array)
		{
			(int i, MeshMerge.FaceBVH f) val = array[last];
			if (compare.Compare(val, array[first]) == 1)
			{
				for (int i = last; i > first; i--)
				{
					array[i] = array[i - 1];
				}
				array[first] = val;
			}
			else
			{
				unguarded_linear_insert(last, val, ref array);
			}
		}

		private void unguarded_linear_insert(int last, (int i, MeshMerge.FaceBVH f) value, ref (int i, MeshMerge.FaceBVH f)[] array)
		{
			int next = last - 1;
			while (compare.Compare(value, array[next]) == 1)
			{
				array[last] = array[next];
				last = next;
				next--;
			}
			array[last] = value;
		}

		private (int i, MeshMerge.FaceBVH f) median_of_3((int i, MeshMerge.FaceBVH f) a, (int i, MeshMerge.FaceBVH f) b, (int i, MeshMerge.FaceBVH f) c)
		{
			if (compare.Compare(a, b) == 1)
			{
				if (compare.Compare(b, c) == 1)
				{
					return b;
				}
				else if (compare.Compare(a, c) == 1)
				{
					return c;
				}
				else
				{
					return a;
				}
			}
			else if (compare.Compare(a, c) == 1)
			{
				return a;
			}
			else if (compare.Compare(b, c) == 1)
			{
				return c;
			}
			else
			{
				return b;
			}
		}

		private void partial_select(int first, int last, int middle, ref (int i, MeshMerge.FaceBVH f)[] array)
		{
			make_heap(first, middle, ref array);
			for (int i = middle; i < last; i++)
			{
				if (compare.Compare(array[i], array[first]) == 1)
				{
					pop_heap(first, middle, i, array[i], ref array);
				}
			}
		}

		private void pop_heap(int first, int last, int result, (int i, MeshMerge.FaceBVH f) value, ref (int i, MeshMerge.FaceBVH f)[] array)
		{
			array[result] = array[first];
			adjust_heap(first, 0, last - first, value, ref array);
		}

		private void pop_heap(int first, int last, ref (int i, MeshMerge.FaceBVH f)[] array)
		{
			pop_heap(first, last - 1, last - 1, array[last - 1], ref array);
		}

		private void make_heap(int first, int last, ref (int i, MeshMerge.FaceBVH f)[] array)
		{
			if (last - first < 2)
			{
				return;
			}
			int len = last - first;
			int parent = (len - 2) / 2;
			while (true)
			{
				adjust_heap(first, parent, len, array[first + parent], ref array);
				if (parent == 0)
				{
					return;
				}
				parent--;
			}
		}

		private void adjust_heap(int first, int hole_idx, int len, (int i, MeshMerge.FaceBVH f) value, ref (int i, MeshMerge.FaceBVH f)[] array)
		{
			int top_index = hole_idx;
			int second_child = 2 * hole_idx + 2;

			while (second_child < len)
			{
				if (compare.Compare(array[first + second_child], array[first + (second_child - 1)]) == 1)
				{
					second_child--;
				}
				array[first + hole_idx] = array[first + second_child];
				hole_idx = second_child;
				second_child = 2 * (second_child + 1);
			}

			if (second_child == len)
			{
				array[first + hole_idx] = array[first + (second_child - 1)];
				hole_idx = second_child - 1;
			}
			push_heap(first, hole_idx, top_index, value, ref array);
		}

		private void push_heap(int first, int hole_idx, int top_index, (int i, MeshMerge.FaceBVH f) value, ref (int i, MeshMerge.FaceBVH f)[] array)
		{
			int parent = (hole_idx - 1) / 2;
			while (hole_idx > top_index && compare.Compare(array[first + parent], value) == 1)
			{
				array[first + hole_idx] = array[first + parent];
				hole_idx = parent;
				parent = (hole_idx - 1) / 2;
			}
			array[first + hole_idx] = value;
		}

		private int partitioner(int first, int last, (int i, MeshMerge.FaceBVH f) pivot, ref (int i, MeshMerge.FaceBVH f)[] array)
		{
			while (true)
			{
				while (compare.Compare(array[first], pivot) == 1)
				{
					first++;
				}
				last--;
				while (compare.Compare(pivot, array[last]) == 1)
				{
					last--;
				}
				if (!(first < last))
				{
					return first;
				}
				(int i, MeshMerge.FaceBVH f) temps = array[first];
				array[first] = array[last];
				array[last] = temps;
				first++;
			}
		}
	}
}