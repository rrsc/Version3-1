﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Chem4Word.ViewModel
{
    public static class GlyphUtils
    {
        public struct GlyphInfo
        {
            public ushort[] Indexes;
            public double Width;
            public double[] AdvanceWidths;

        }

        private static GlyphTypeface _glyphTypeface;

        public static GlyphTypeface GlyphTypeface
        {
            get
            {
                return _glyphTypeface;
            }
        }
        public static double SymbolSize = 26;
        public static double ScriptSize = SymbolSize * 0.6;
        public static double IsotopeSize = ScriptSize * 0.8;


        public static Typeface SymbolTypeface = new Typeface(new FontFamily("Arial"),
            FontStyles.Normal,
            FontWeights.Bold,
            FontStretches.Normal);
        static GlyphUtils()
        {
            if (!SymbolTypeface.TryGetGlyphTypeface(out _glyphTypeface))
            {
                throw new InvalidOperationException("No glyphtypeface found");
            }
        }

        //public struct LayoutInfo
        //{
        //    public GlyphRun Run;
        //    public Point NewOrigin;
        //    public ushort[] Indexes;
        //    public double Width;
        //    public double[] AdvanceWidths;

        //}
       

        /// <summary>
        /// Gets the vector that must be added to the atom position to center the glyph
        /// </summary>
        /// <param name="glyphRun">Run of text for atom symbol</param>
        /// <param name="symbolSize">Size of symbol in DIPs</param>
        /// <returns>Vector to be added to atom pos</returns>
        public static Vector GetOffsetVector(GlyphRun glyphRun, double symbolSize)
        {
            Rect rect = glyphRun.ComputeInkBoundingBox();

            //Vector offset = (rect.BottomLeft - rect.TopRight) / 2;
            Vector offset = new Vector(-rect.Width / 2, glyphRun.GlyphTypeface.CapsHeight * symbolSize / 2);
            return offset;
        }

        /// <summary>
        /// Returns a glyph run for a given string of text
        /// </summary>
        /// <param name="symbolText">Text for the atom symbol</param>
        /// <param name="glyphTypeFace">Glyph type face used</param>
        /// <param name="size">Size in DIPS of the font</param>
        /// <returns>GlyphInfo of  glyph indexes, overall width and array of advance widths</returns>
        public static
            GlyphInfo
            GetGlyphs(string symbolText, GlyphTypeface glyphTypeFace, double size)
        {



            ushort[] glyphIndexes = new ushort[symbolText.Length];
            double[] advanceWidths = new double[symbolText.Length];

            double totalWidth = 0;

            for (int n = 0; n < symbolText.Length; n++)
            {
                ushort glyphIndex = glyphTypeFace.CharacterToGlyphMap[symbolText[n]];
                glyphIndexes[n] = glyphIndex;

                double width = glyphTypeFace.AdvanceWidths[glyphIndex] * size;
                advanceWidths[n] = width;

                totalWidth += width;
            }

            return new GlyphInfo {AdvanceWidths = advanceWidths, Indexes = glyphIndexes, Width = totalWidth};

        }

        public static GlyphUtils.GlyphInfo GetGlyphsAndInfo(string symbolText, float pixelsPerDip, out GlyphRun hydrogenGlyphRun, Point point, GlyphTypeface glyphTypeFace, double symbolSize)
        {
            //measure the H atom first
            var glyphInfo = GlyphUtils.GetGlyphs(symbolText, glyphTypeFace, symbolSize);
            hydrogenGlyphRun = GlyphUtils.GetGlyphRun(glyphInfo, glyphTypeFace,
                symbolSize, pixelsPerDip, point);
            //work out exactly how much we should offset from the center to get to the bottom left
            return glyphInfo;
        }

        /// <summary>
        /// Returns a rough outline of a glyph run.  useful for calculating a convex hull
        /// </summary>
        /// <param name="glyphRun">Glyph run to outline</param>
        /// <returns>List<Point> of geomtry tracing the GlyphRun</Point></returns>
        public static List<Point> GetOutline(this GlyphRun glyphRun)
        {
            List<Point> retval = new List<Point>();

            if (glyphRun != null)
            {
                var geo = glyphRun.BuildGeometry();
                var pg = geo.GetFlattenedPathGeometry(0.2, ToleranceType.Relative);

               
                foreach (var f in pg.Figures)
                {
                    foreach (var s in f.Segments)
                    {
                        if (s is PolyLineSegment)
                        {
                            foreach (var pt in ((PolyLineSegment)s).Points)
                            {
                                retval.Add(pt);
                            }
                        }
                    }
                }
            }
            return retval;
        }
        /// <summary>
        /// Generates a subscript for a glyph run
        /// </summary>
        /// <param name="script">text of subscript</param>
        /// <param name="gtf">GlphTypeFace used</param>
        /// <param name="subscriptSize">size of scubscript (generally 60% of main text)</param>
        /// <param name="bottomLeft">starting point for placing the subscript</param>
        /// <returns></returns>
      

        /// <summary>
        /// gets a bounding box holding the overall glyph run
        /// </summary>
        /// <param name="glyphRun"></param>
        /// <param name="origin">where the run will be centered</param>
        /// <returns></returns>
        /// 
    
        public static Rect GetBoundingBox(this GlyphRun glyphRun, Point origin)
        {
            Rect rect = glyphRun.ComputeInkBoundingBox();
            TranslateTransform tt = new TranslateTransform(origin.X, origin.Y);
            Matrix mat = new Matrix();
            mat.Translate(origin.X, origin.Y);

            rect.Transform(mat);
            return rect;
        }

        /// <summary>
        /// simple wrapper routine for generating a glyph run
        /// </summary>
        /// <param name="info"></param>
        /// <param name="glyphTypeface"></param>
        /// <param name="symbolSize"></param>
        /// <param name="pixelsPerDip"></param>
        /// <param name="point"></param>
        /// <returns></returns>
        public static GlyphRun GetGlyphRun(GlyphInfo info, GlyphTypeface glyphTypeface, double symbolSize, float pixelsPerDip, Point point)
        {
            
            var run = new GlyphRun(glyphTypeface, 0, false, symbolSize, pixelsPerDip, info.Indexes, point, info.AdvanceWidths,
                null,null,null,null,null,null);

            return run;
        }


    }
}
