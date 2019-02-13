﻿// ---------------------------------------------------------------------------
//  Copyright (c) 2019, The .NET Foundation.
//  This software is released under the Apache License, Version 2.0.
//  The license and further copyright text can be found in the file LICENSE.md
//  at the root directory of the distribution.
// ---------------------------------------------------------------------------

using System.Linq;

using Chem4Word.Model2;
using Chem4Word.ViewModel;
using Chem4Word.ViewModel.Commands;

namespace Chem4Word.ACME.Commands
{
    public class AddHydrogensCommand : BaseCommand
    {
        public AddHydrogensCommand(EditViewModel vm) : base(vm)
        {
        }

        public override bool CanExecute(object parameter)
        {
            var mols = MyEditViewModel.SelectedItems.OfType<Molecule>().ToList();
            var atoms = MyEditViewModel.SelectedItems.OfType<Atom>().ToList();
            var bonds = MyEditViewModel.SelectedItems.OfType<Bond>().ToList();
            var nothingSelected = MyEditViewModel.SelectedItems.Count == 0;

            return nothingSelected || mols.Any() && !atoms.Any() && !bonds.Any();
        }

        public override void Execute(object parameter)
        {
            MyEditViewModel.AddHydrogens();
        }
    }
}