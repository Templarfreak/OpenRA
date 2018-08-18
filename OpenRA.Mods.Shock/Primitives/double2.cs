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
	public struct double2 : IEquatable<double2>
	{
		public readonly double X, Y;

		public double2(double x, double y) { X = x; Y = y; }
		public double2(PointF p) { X = p.X; Y = p.Y; }
		public double2(Point p) { X = p.X; Y = p.Y; }
		public double2(Size p) { X = p.Width; Y = p.Height; }
		public double2(SizeF p) { X = p.Width; Y = p.Height; }

		public PointF ToPointF() { return new PointF((float)X, (float)Y); }
		public SizeF ToSizeF() { return new SizeF((float)X, (float)Y); }

		public float2 ToFloat2() { return new float2((float)X, (float)Y); }

		public static implicit operator double2(int2 src) { return new double2(src.X, src.Y); }

		public static double2 operator +(double2 a, double2 b) { return new double2(a.X + b.X, a.Y + b.Y); }
		public static double2 operator -(double2 a, double2 b) { return new double2(a.X - b.X, a.Y - b.Y); }

		public static double2 operator -(double2 a) { return new double2(-a.X, -a.Y); }

		public static double Lerp(double a, double b, double t) { return a + t * (b - a); }

		public static double2 Lerp(double2 a, double2 b, double t)
		{
			return new double2(
				Lerp(a.X, b.X, t),
				Lerp(a.Y, b.Y, t));
		}

		public static double2 Lerp(double2 a, double2 b, double2 t)
		{
			return new double2(
				Lerp(a.X, b.X, t.X),
				Lerp(a.Y, b.Y, t.Y));
		}

		public static double2 FromAngle(double a) { return new double2((double)Math.Sin(a), (double)Math.Cos(a)); }

		static double Constrain(double x, double a, double b) { return x < a ? a : x > b ? b : x; }

		public double2 Constrain(double2 min, double2 max)
		{
			return new double2(
				Constrain(X, min.X, max.X),
				Constrain(Y, min.Y, max.Y));
		}

		public static double2 operator *(double a, double2 b) { return new double2(a * b.X, a * b.Y); }
		public static double2 operator *(double2 b, double a) { return new double2(a * b.X, a * b.Y); }
		public static double2 operator *(double2 a, double2 b) { return new double2(a.X * b.X, a.Y * b.Y); }
		public static double2 operator /(double2 a, double2 b) { return new double2(a.X / b.X, a.Y / b.Y); }
		public static double2 operator /(double2 a, double b) { return new double2(a.X / b, a.Y / b); }

		public static bool operator ==(double2 me, double2 other) { return me.X == other.X && me.Y == other.Y; }
		public static bool operator !=(double2 me, double2 other) { return !(me == other); }

		public override int GetHashCode() { return X.GetHashCode() ^ Y.GetHashCode(); }

		public bool Equals(double2 other) { return this == other; }
		public override bool Equals(object obj) { return obj is double2 && Equals((double2)obj); }

		public override string ToString() { return X + "," + Y; }

		public static readonly double2 Zero = new double2(0, 0);

		public static bool WithinEpsilon(double2 a, double2 b, double e)
		{
			var d = a - b;
			return Math.Abs(d.X) < e && Math.Abs(d.Y) < e;
		}

		public double2 Sign() { return new double2(Math.Sign(X), Math.Sign(Y)); }
		public static double Dot(double2 a, double2 b) { return a.X * b.X + a.Y * b.Y; }
		public double2 Round() { return new double2((double)Math.Round(X), (double)Math.Round(Y)); }

		public int2 ToInt2() { return new int2((int)X, (int)Y); }

		public static double2 Max(double2 a, double2 b) { return new double2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)); }
		public static double2 Min(double2 a, double2 b) { return new double2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)); }

		public double LengthSquared { get { return X * X + Y * Y; } }
		public double Length { get { return (double)Math.Sqrt(LengthSquared); } }
	}

	public class EWMA
	{
		readonly double animRate;
		double? value;

		public EWMA(double animRate) { this.animRate = animRate; }

		public double Update(double newValue)
		{
			value = double2.Lerp(value ?? newValue, newValue, animRate);
			return value.Value;
		}
	}
}
