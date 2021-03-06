﻿// ---------------------------------------------------------------------------
//  Copyright (c) 2020, The .NET Foundation.
//  This software is released under the Apache License, Version 2.0.
//  The license and further copyright text can be found in the file LICENSE.md
//  at the root directory of the distribution.
// ---------------------------------------------------------------------------

using Chem4Word.Model2;

namespace Chem4Word.ACME
{
    public class ViewModel
    {
        public ViewModel(Model chemistryModel)
        {
            Model = chemistryModel;
        }

        #region Properties

        public Model Model { get; }

        #endregion Properties
    }
}