﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Win32;
//using System.Linq;

using Au.Types;

namespace Au
{
	/// <summary>
	/// Simple calculation functions.
	/// </summary>
	//[DebuggerStepThrough]
	public static class AMath
	{
		/// <summary>
		/// Creates uint by placing (ushort)loWord in bits 1-16 and (ushort)hiWord in bits 17-32.
		/// Like C macro MAKELONG, MAKEWPARAM, MAKELPARAM, MAKELRESULT.
		/// </summary>
		public static uint MakeUint(uint loWord, uint hiWord) => ((hiWord & 0xffff) << 16) | (loWord & 0xffff);
		
		/// <summary>
		/// Creates uint by placing (ushort)loWord in bits 1-16 and (ushort)hiWord in bits 17-32.
		/// Like C macro MAKELONG, MAKEWPARAM, MAKELPARAM, MAKELRESULT.
		/// </summary>
		public static uint MakeUint(int loWord, int hiWord) => MakeUint((uint)loWord, (uint)hiWord);

		/// <summary>
		/// Creates ushort by placing (byte)loByte in bits 1-8 and (byte)hiByte in bits 9-16.
		/// Like C macro MAKEWORD.
		/// </summary>
		public static ushort MakeUshort(uint loByte, uint hiByte) => (ushort)(((hiByte & 0xff) << 8) | (loByte & 0xff));
		
		/// <summary>
		/// Creates ushort by placing (byte)loByte in bits 1-8 and (byte)hiByte in bits 9-16.
		/// Like C macro MAKEWORD.
		/// </summary>
		public static ushort MakeUshort(int loByte, int hiByte) => MakeUshort((uint)loByte, (uint)hiByte);

		/// <summary>
		/// Gets bits 1-16 as ushort.
		/// Like C macro LOWORD.
		/// </summary>
		/// <remarks>
		/// The parameter is interpreted as uint. Its declared type is LPARAM because it allows to avoid explicit casting from other integer types and IntPtr (casting from IntPtr to uint could throw OverflowException).
		/// </remarks>
		public static ushort LoUshort(LPARAM x) => (ushort)((uint)x & 0xFFFF);

		/// <summary>
		/// Gets bits 17-32 as ushort.
		/// Like C macro HIWORD.
		/// </summary>
		/// <remarks>
		/// The parameter is interpreted as uint. Its declared type is LPARAM because it allows to avoid explicit casting from other integer types and IntPtr (casting from IntPtr to uint could throw OverflowException).
		/// </remarks>
		public static ushort HiUshort(LPARAM x) => (ushort)((uint)x >> 16);

		/// <summary>
		/// Gets bits 1-16 as short.
		/// Like C macro GET_X_LPARAM.
		/// </summary>
		/// <remarks>
		/// The parameter is interpreted as uint. Its declared type is LPARAM because it allows to avoid explicit casting from other integer types and IntPtr (casting from IntPtr to uint could throw OverflowException).
		/// </remarks>
		public static short LoShort(LPARAM x) => (short)((uint)x & 0xFFFF);

		/// <summary>
		/// Gets bits 17-32 as short.
		/// Like C macro GET_Y_LPARAM.
		/// </summary>
		/// <remarks>
		/// The parameter is interpreted as uint. Its declared type is LPARAM because it allows to avoid explicit casting from other integer types and IntPtr (casting from IntPtr to uint could throw OverflowException).
		/// </remarks>
		public static short HiShort(LPARAM x) => (short)((uint)x >> 16);

		/// <summary>
		/// Gets bits 1-8 as byte.
		/// Like C macro LOBYTE.
		/// </summary>
		public static byte LoByte(ushort x) => (byte)((uint)x & 0xFF);

		/// <summary>
		/// Gets bits 9-16 as byte.
		/// Like C macro HIBYTE.
		/// </summary>
		public static byte HiByte(ushort x) => (byte)((uint)x >> 8);

		/// <summary>
		/// Multiplies number and numerator without overflow, and divides by denominator.
		/// The return value is rounded up or down to the nearest integer.
		/// If either an overflow occurred or denominator was 0, the return value is –1.
		/// </summary>
		public static int MulDiv(int number, int numerator, int denominator) => Api.MulDiv(number, numerator, denominator);
		//=> (int)(((long)number * numerator) / denominator);
		//could use this, but the API rounds down or up to the nearest integer, but this always rounds down.

		/// <summary>
		/// Returns percent of part in whole.
		/// </summary>
		public static int Percent(int whole, int part) => (int)(100L * part / whole);

		/// <summary>
		/// Returns percent of part in whole.
		/// </summary>
		public static double Percent(double whole, double part) => 100.0 * part / whole;

		/// <summary>
		/// If value is divisible by alignment, returns value. Else returns nearest bigger number that is divisible by alignment.
		/// </summary>
		/// <param name="value">An integer value.</param>
		/// <param name="alignment">Alignment. Must be a power of two (2, 4, 8, 16...).</param>
		/// <remarks>For example if alignment is 4, returns 4 if value is 1-4, returns 8 if value is 5-8, returns 12 if value is 9-10, and so on.</remarks>
		public static uint AlignUp(uint value, uint alignment) => (value + (alignment - 1)) & ~(alignment - 1);

		/// <summary>
		/// If value is divisible by alignment, returns value. Else returns nearest bigger number that is divisible by alignment.
		/// </summary>
		/// <param name="value">An integer value.</param>
		/// <param name="alignment">Alignment. Must be a power of two (2, 4, 8, 16...).</param>
		/// <remarks>For example if alignment is 4, returns 4 if value is 1-4, returns 8 if value is 5-8, returns 12 if value is 9-10, and so on.</remarks>
		public static int AlignUp(int value, uint alignment) => (int)AlignUp((uint)value, alignment);

		/// <summary>
		/// Returns value but not less than min and not greater than max.
		/// If value is less than min, returns min.
		/// If value is greater than max, returns max.
		/// </summary>
		public static int MinMax(int value, int min, int max)
		{
			Debug.Assert(max >= min);
			if(value < min) return min;
			if(value > max) return max;
			return value;
		}

		/// <summary>
		/// Swaps values of variables a and b: <c>T t = a; a = b; b = t;</c>
		/// </summary>
		public static void Swap<T>(ref T a, ref T b)
		{
			T t = a; a = b; b = t;
		}

		/// <summary>
		/// Calculates angle degrees from coordinates x and y.
		/// </summary>
		public static double AngleFromXY(int x, int y) => Math.Atan2(y, x) * (180 / Math.PI);

		/// <summary>
		/// Calculates distance between two points.
		/// </summary>
		public static double Distance(POINT p1, POINT p2)
		{
			if(p1.y == p2.y) return Math.Abs(p2.x - p1.x); //horizontal line
			if(p1.x == p2.x) return Math.Abs(p2.y - p1.y); //vertical line

			long dx = p2.x - p1.x, dy = p2.y - p1.y;
			return Math.Sqrt(dx * dx + dy * dy);
		}

		/// <summary>
		/// Calculates distance between rectangle and point.
		/// Returns 0 if point is in rectangle.
		/// </summary>
		public static double Distance(RECT r, POINT p)
		{
			r.Normalize(swap: true);
			if(r.Contains(p)) return 0;
			int x = p.x < r.left ? r.left : (p.x > r.right ? r.right : p.x);
			int y = p.y < r.top ? r.top : (p.y > r.bottom ? r.bottom : p.y);
			return Distance((x, y), p);
		}
	}
}
