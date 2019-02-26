﻿// ---------------------------------------------------------------------------
//  Copyright (c) 2019, The .NET Foundation.
//  This software is released under the Apache License, Version 2.0.
//  The license and further copyright text can be found in the file LICENSE.md
//  at the root directory of the distribution.
// ---------------------------------------------------------------------------

using System;

namespace Chem4Word.Model2.Helpers
{
    public static class AtomHelpers
    {
        public static string GetSubText(int hCount = 0)
        {
            string mult = "";
            int i;
            if ((i = Math.Abs(hCount)) > 1)
            {
                mult = i.ToString();
            }

            return mult;
        }

        /// <summary>
        /// gets the charge annotation string for an atom symbol
        /// </summary>
        /// <param name="charge">Int contaioning the charge value</param>
        /// <returns></returns>
        public static string GetChargeString(int? charge)
        {
            string chargestring = "";

            if ((charge ?? 0) > 0)
            {
                chargestring = "+";
            }
            if ((charge ?? 0) < 0)
            {
                chargestring = "-";
            }
            int abscharge = 0;
            if ((abscharge = Math.Abs(charge ?? 0)) > 1)
            {
                chargestring = abscharge.ToString() + chargestring;
            }
            return chargestring;
        }

        public static bool TryParse(string text, out ElementBase result)
        {
            bool success = false;
            result = null;

            if (FunctionalGroup.TryParse(text, out FunctionalGroup fg))
            {
                result = fg;
                success = true;
            }
            else if (Globals.PeriodicTable.HasElement(text))
            {
                result = Globals.PeriodicTable.Elements[text];
                success = true;
            }

            return success;
        }
    }
}