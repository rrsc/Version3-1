﻿// ---------------------------------------------------------------------------
//  Copyright (c) 2018, The .NET Foundation.
//  This software is released under the Apache License, Version 2.0.
//  The license and further copyright text can be found in the file LICENSE.md
//  at the root directory of the distribution.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Chem4Word.ViewModel
{
    /// <summary>
    /// Manages undo/redo for a view model
    /// All actions to be recorded should be
    /// packaged as a pair of actions, for both undo and redo
    /// Every set of actions MUST be nested between
    /// BeginTrans() and CommitTrans() calls
    /// 
    /// </summary>
    public class UndoHandler
    {

        private struct UndoRecord
        {
            public int Level;
            public string Description;
            public Action UndoAction;
            public Action RedoAction;

            public void Undo()
            { 
                UndoAction();
            }

            public void Redo()
            {
                RedoAction();
            }

            public bool IsBufferRecord()
            {
                return Level == 0;
            }
        }

        //each block of transactions is bracketed by a buffer record at either end
        private readonly UndoRecord _bufferRecord;

        private EditViewModel _editViewModel;

        private Stack<UndoRecord> _undoStack;
        private Stack<UndoRecord> _redoStack;

        private int _transactionLevel = 0;

        public int TransactionLevel => _transactionLevel;

            
  
        public bool CanRedo => _redoStack.Any(rr => rr.Level!=0);

        public bool CanUndo => _undoStack.Any(ur => ur.Level != 0);


        
        public UndoHandler(EditViewModel vm)
        {
            _editViewModel = vm;

            //set up the buffer record
            _bufferRecord = new UndoRecord
            {
                Description = "#buffer#",
                Level = 0,
                UndoAction = null,
                RedoAction = null
            };

            Initialize();
        }
        public void Initialize()
        {
            _undoStack = new Stack<UndoRecord>();
            _redoStack = new Stack<UndoRecord>();
        }

        public void BeginTrans()
        {
            //push a buffer record onto the stack
            if (_transactionLevel == 0)
            {
                _undoStack.Push(_bufferRecord);
            }
            _transactionLevel++;

        }

        public void RecordAction(string desc, 
            Action  undoAction, 
            Action  redoAction)
        {
            //performing a new action should clear the redo
            if (_redoStack.Any())
            {
                _redoStack.Clear();
            }
            _undoStack.Push(new UndoRecord {Level = _transactionLevel,
                Description = desc, UndoAction = undoAction,
                RedoAction = redoAction
                });
        }


        /// <summary>
        /// Ends a transaction block.  Transactions may be nested
        /// </summary>
        public void CommitTrans()
        {
            _transactionLevel--;
            
            if ( _transactionLevel < 0)
            {
                throw new IndexOutOfRangeException("Attempted to unwind empty undo stack.");
            }

            //we've concluded a transaction block so terminated it
            if (_transactionLevel == 0)
            {
                _undoStack.Push(_bufferRecord);
            }
            //tell the parent viewmodel the command status has changed
            _editViewModel.UndoCommand.RaiseCanExecChanged();
            _editViewModel.RedoCommand.RaiseCanExecChanged();
        }

        public void Undo()
        {
            UndoActions();
            _editViewModel.UndoCommand.RaiseCanExecChanged();
            _editViewModel.RedoCommand.RaiseCanExecChanged();
        }

        private void UndoActions()
        {
            //the very first record on the undo stack should be a buffer record
            var br = _undoStack.Pop();
            if (!br.IsBufferRecord())
            {
                throw new InvalidDataException("Undo stack is missing buffer record");
            }
            _redoStack.Push(br);

            while(true)
            {
                br = _undoStack.Pop();
                _redoStack.Push(br);
                if (br.IsBufferRecord())
                {
                    break;
                }
                br.Undo();
                
            } 

        }

        public void Redo()
        {
            RedoActions();
            _editViewModel.UndoCommand.RaiseCanExecChanged();
            _editViewModel.RedoCommand.RaiseCanExecChanged();
        }

        private void RedoActions()
        {
            //the very first record on the redo stack should be a buffer record
            var br = _redoStack.Pop();
            if (!br.IsBufferRecord())
            {
                throw new InvalidDataException("Redo stack is missing buffer record");
            }
            _undoStack.Push(br);

            while (true)
            {
                br = _redoStack.Pop();
                _undoStack.Push(br);
                if (br.IsBufferRecord())
                {
                    break;
                }
                br.Redo();
               
            } 
        }
    }
}