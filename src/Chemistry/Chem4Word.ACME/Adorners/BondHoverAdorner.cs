﻿// ---------------------------------------------------------------------------
//  Copyright (c) 2019, The .NET Foundation.
//  This software is released under the Apache License, Version 2.0.
//  The license and further copyright text can be found in the file LICENSE.md
//  at the root directory of the distribution.
// ---------------------
using Chem4Word.ACME.Drawing;
using Chem4Word.Model2;
using Chem4Word.Model2.Annotations;
using Chem4Word.Model2.Geometry;
using Chem4Word.Model2.Helpers;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Chem4Word.ACME.Adorners
{
    public class BondHoverAdorner : Adorner
    {
        private SolidColorBrush _solidColorBrush;
        private Pen _bracketPen;
        private BondVisual _targetedVisual;
        private Bond _targetedBond;

        public BondHoverAdorner([NotNull] UIElement adornedElement) : base(adornedElement)
        {
            _solidColorBrush = new SolidColorBrush(Globals.HoverAdornerColor);
            //_solidColorBrush.Opacity = 0.25;

            
            _bracketPen = new Pen(_solidColorBrush, Globals.HoverAdornerThickness);

            var myAdornerLayer = AdornerLayer.GetAdornerLayer(adornedElement);
            myAdornerLayer.Add(this);
        }

        public BondHoverAdorner(UIElement adornedElement, BondVisual targetedVisual) : this(adornedElement)
        {
            _targetedVisual = targetedVisual;
            _targetedBond = _targetedVisual.ParentBond;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            StreamGeometry sg = new StreamGeometry();
            double offset = Globals.BondOffsetPercentage * _targetedBond.BondLength * _targetedBond.OrderValue ?? 1.0;

            //this tells us how much to rotate the brackets at the end of the bond
            double bondAngle = _targetedBond.Angle;

            Vector offsetVector1 = new Vector(offset, 0d);

            Matrix rotator = new Matrix();
            rotator.Rotate(bondAngle);

            offsetVector1 = offsetVector1 * rotator;

            Vector twiddle = - offsetVector1.Perpendicular();
            twiddle.Normalize();
            twiddle *= 3.0;

            using (StreamGeometryContext sgc = sg.Open())
            {
                sgc.BeginFigure(_targetedBond.StartAtom.Position+ offsetVector1 + twiddle, false, false);
                sgc.LineTo(_targetedBond.StartAtom.Position + offsetVector1, true,true);
                sgc.LineTo(_targetedBond.StartAtom.Position- offsetVector1, true, true);
                sgc.LineTo(_targetedBond.StartAtom.Position-offsetVector1 + twiddle,true,true);

                sgc.BeginFigure(_targetedBond.EndAtom.Position + offsetVector1 - twiddle, false, false);
                sgc.LineTo(_targetedBond.EndAtom.Position + offsetVector1, true, true);
                sgc.LineTo(_targetedBond.EndAtom.Position - offsetVector1, true, true);
                sgc.LineTo(_targetedBond.EndAtom.Position - offsetVector1 - twiddle, true, true);


                sgc.Close();
            }

            drawingContext.DrawGeometry(_solidColorBrush, _bracketPen, sg);
        }
    }
}