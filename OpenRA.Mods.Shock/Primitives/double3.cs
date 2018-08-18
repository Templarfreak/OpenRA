#region Copyright & License Information
/*
 * Copyright 2007-2018 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.InteropServices;

namespace OpenRA
{
	[SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:ElementMustBeginWithUpperCaseLetter", Justification = "Mimic a built-in type alias.")]
	[StructLayout(LayoutKind.Sequential)]
	public struct double3
	{
		public double X, Y, Z;
		public double2 XY { get { return new double2(X, Y); } }

		public double3(double x, double y, double z) { X = x; Y = y; Z = z; }
		public double3(double2 xy, double z) { X = xy.X; Y = xy.Y; Z = z; }

		public static implicit operator double3(int2 src) { return new double3(src.X, src.Y, 0); }
		public static implicit operator double3(double2 src) { return new double3(src.X, src.Y, 0); }

		public static double3 operator +(double3 a, double3 b) { return new double3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
		public static double3 operator -(double3 a, double3 b) { return new double3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
		public static double3 operator -(double3 a) { return new double3(-a.X, -a.Y, -a.Z); }
		public static double3 operator *(double3 a, double3 b) { return new double3(a.X * b.X, a.Y * b.Y, a.Z * b.Z); }
		public static double3 operator *(double a, double3 b) { return new double3(a * b.X, a * b.Y, a * b.Z); }
		public static double3 operator /(double3 a, double3 b) { return new double3(a.X / b.X, a.Y / b.Y, a.Z / b.Z); }
		public static double3 operator /(double3 a, double b) { return new double3(a.X / b, a.Y / b, a.Z / b); }

		public static double3 Lerp(double3 a, double3 b, double t)
		{
			return new double3(
				double2.Lerp(a.X, b.X, t),
				double2.Lerp(a.Y, b.Y, t),
				double2.Lerp(a.Z, b.Z, t));
		}

		public static bool operator ==(double3 me, double3 other) { return me.X == other.X && me.Y == other.Y && me.Z == other.Z; }
		public static bool operator !=(double3 me, double3 other) { return !(me == other); }
		public override int GetHashCode() { return X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode(); }

		public override bool Equals(object obj)
		{
			var o = obj as double3?;
			return o != null && o == this;
		}

		public override string ToString() { return "{0},{1},{2}".F(X, Y, Z); }
		public float3 ToFloat3() { return new float3((float)X, (float)Y, (float)Z); }
		public double3 ToDouble3(float3 f3) { return new double3(f3.X, f3.Y, f3.Z); }
		public Color ToColor() { return Color.FromArgb((byte)X, (byte)Y, (byte)Z); }

		public static readonly double3 Zero = new double3(0, 0, 0);
	}
}
