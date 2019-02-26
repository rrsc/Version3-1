﻿// ---------------------------------------------------------------------------
//  Copyright (c) 2019, The .NET Foundation.
//  This software is released under the Apache License, Version 2.0.
//  The license and further copyright text can be found in the file LICENSE.md
//  at the root directory of the distribution.
// ---------------------------------------------------------------------------

using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace Chem4Word.ACME.Drawing
{
    public class LabelTextRunProperties : TextRunProperties
    {
        public override System.Windows.Media.Brush BackgroundBrush
        {
            get { return null; }
        }

        public override CultureInfo CultureInfo
        {
            get { return CultureInfo.CurrentCulture; }
        }

        public override double FontHintingEmSize
        {
            get { return GlyphText.SymbolSize; }
        }

        public override double FontRenderingEmSize
        {
            get { return GlyphText.SymbolSize; }
        }

        public override Brush ForegroundBrush
        {
            get { return Brushes.Black; }
        }

        public override System.Windows.TextDecorationCollection TextDecorations
        {
            get { return new System.Windows.TextDecorationCollection(); }
        }

        public override System.Windows.Media.TextEffectCollection TextEffects
        {
            get { return new TextEffectCollection(); }
        }

        public override System.Windows.Media.Typeface Typeface
        {
            get { return GlyphUtils.SymbolTypeface; }
        }

        public override TextRunTypographyProperties TypographyProperties
        {
            get
            {
                return new LabelTextRunTypographyProperties();
            }
        }
    }
}