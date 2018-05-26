﻿// ---------------------------------------------------------------------------
//  Copyright (c) 2018, The .NET Foundation.
//  This software is released under the Apache License, Version 2.0.
//  The license and further copyright text can be found in the file LICENSE.md
//  at the root directory of the distribution.
// ---------------------------------------------------------------------------

namespace Chem4Word.ViewModel.Commands
{
    public class RemoveHydrogensCommand : BaseCommand
    {
        public RemoveHydrogensCommand(EditViewModel vm) : base(vm)
        {
        }

        public override bool CanExecute(object parameter)
        {
            return MyEditViewModel.SelectionType != EditViewModel.SelectionTypeCode.None;
        }

        public override void Execute(object parameter)
        {
            MyEditViewModel.RemoveHydrogens();
        }
    }
}
