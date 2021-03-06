﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using ShaderTools.CodeAnalysis.Editor;
using ShaderTools.CodeAnalysis.Editor.Commands;
using ShaderTools.Utilities.Diagnostics;

namespace ShaderTools.VisualStudio.LanguageServices.Implementation
{
    internal abstract partial class AbstractOleCommandTarget
    {
        private const int ECMD_SMARTTASKS = 147;

        public int QueryStatus(ref Guid pguidCmdGroup, uint commandCount, OLECMD[] prgCmds, IntPtr commandText)
        {
            Contract.ThrowIfFalse(commandCount == 1);
            Contract.ThrowIfFalse(prgCmds.Length == 1);

            // TODO: We'll need to extend the command handler interfaces at some point when we have commands that
            // require enabling/disabling at some point.  For now, we just enable the few that we care about.
            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                return QueryVisualStudio2000Status(ref pguidCmdGroup, commandCount, prgCmds, commandText);
            }
            else if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97)
            {
                return QueryVisualStudio97Status(ref pguidCmdGroup, commandCount, prgCmds, commandText);
            }
            else if (pguidCmdGroup == VSConstants.GUID_AppCommand)
            {
                return QueryAppCommandStatus(ref pguidCmdGroup, commandCount, prgCmds, commandText);
            }
            else
            {
                return NextCommandTarget.QueryStatus(ref pguidCmdGroup, commandCount, prgCmds, commandText);
            }
        }

        private int QueryAppCommandStatus(ref Guid pguidCmdGroup, uint commandCount, OLECMD[] prgCmds, IntPtr commandText)
        {
            switch ((VSConstants.AppCommandCmdID) prgCmds[0].cmdID)
            {
                case VSConstants.AppCommandCmdID.BrowserBackward:
                case VSConstants.AppCommandCmdID.BrowserForward:
                    prgCmds[0].cmdf = (uint) (OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
                    return VSConstants.S_OK;

                default:
                    return NextCommandTarget.QueryStatus(ref pguidCmdGroup, commandCount, prgCmds, commandText);
            }
        }

        private int QueryVisualStudio97Status(ref Guid pguidCmdGroup, uint commandCount, OLECMD[] prgCmds, IntPtr commandText)
        {
            switch ((VSConstants.VSStd97CmdID) prgCmds[0].cmdID)
            {
                case VSConstants.VSStd97CmdID.GotoDefn:
                    return QueryGoToDefinitionStatus(prgCmds);

                default:
                    return NextCommandTarget.QueryStatus(ref pguidCmdGroup, commandCount, prgCmds, commandText);
            }
        }

        private int QueryVisualStudio2000Status(ref Guid pguidCmdGroup, uint commandCount, OLECMD[] prgCmds, IntPtr commandText)
        {
            switch ((VSConstants.VSStd2KCmdID) prgCmds[0].cmdID)
            {
                case VSConstants.VSStd2KCmdID.FORMATDOCUMENT:
                    return QueryFormatDocumentStatus(prgCmds);

                case VSConstants.VSStd2KCmdID.FORMATSELECTION:
                    return QueryFormatSelectionStatus(prgCmds);

                case CmdidToggleConsumeFirstMode:
                    return QueryToggleConsumeFirstModeStatus(ref pguidCmdGroup, commandCount, prgCmds, commandText);

                case VSConstants.VSStd2KCmdID.COMMENT_BLOCK:
                case VSConstants.VSStd2KCmdID.COMMENTBLOCK:
                    return QueryCommentBlockStatus(ref pguidCmdGroup, commandCount, prgCmds, commandText);

                case VSConstants.VSStd2KCmdID.UNCOMMENT_BLOCK:
                case VSConstants.VSStd2KCmdID.UNCOMMENTBLOCK:
                    return QueryUncommentBlockStatus(ref pguidCmdGroup, commandCount, prgCmds, commandText);

                case CmdidNextHighlightedReference:
                case CmdidPreviousHighlightedReference:
                    return QueryNavigateHighlightedReferenceStatus(prgCmds);

                case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                    return QueryCompleteWordStatus(prgCmds);

                case VSConstants.VSStd2KCmdID.SHOWMEMBERLIST:
                    return QueryShowMemberListStatus(prgCmds);

                case VSConstants.VSStd2KCmdID.PARAMINFO:
                    return QueryParameterInfoStatus(prgCmds);

                case VSConstants.VSStd2KCmdID.QUICKINFO:
                    return QueryQuickInfoStatus(prgCmds);

                case VSConstants.VSStd2KCmdID.OUTLN_START_AUTOHIDING:
                    return QueryStartAutomaticOutliningStatus(ref pguidCmdGroup, commandCount, prgCmds, commandText);

                case VSConstants.VSStd2KCmdID.OPENFILE:
                    return QueryOpenFileStatus(ref pguidCmdGroup, commandCount, prgCmds, commandText);

                default:
                    return NextCommandTarget.QueryStatus(ref pguidCmdGroup, commandCount, prgCmds, commandText);
            }
        }

        private int GetCommandState<T>(
            Func<ITextView, ITextBuffer, T> createArgs,
            ref Guid pguidCmdGroup,
            uint commandCount,
            OLECMD[] prgCmds,
            IntPtr commandText)
            where T : CommandArgs
        {
            var result = VSConstants.S_OK;

            var guidCmdGroup = pguidCmdGroup;
            Func<CommandState> executeNextCommandTarget = () =>
            {
                result = NextCommandTarget.QueryStatus(ref guidCmdGroup, commandCount, prgCmds, commandText);

                var isAvailable = ((OLECMDF) prgCmds[0].cmdf & OLECMDF.OLECMDF_ENABLED) == OLECMDF.OLECMDF_ENABLED;
                var isChecked = ((OLECMDF) prgCmds[0].cmdf & OLECMDF.OLECMDF_LATCHED) == OLECMDF.OLECMDF_LATCHED;
                return new CommandState(isAvailable, isChecked, GetText(commandText));
            };

            CommandState commandState;
            var subjectBuffer = GetSubjectBufferContainingCaret();
            if (subjectBuffer == null)
            {
                commandState = executeNextCommandTarget();
            }
            else
            {
                commandState = CurrentHandlers.GetCommandState<T>(
                    subjectBuffer.ContentType,
                    args: createArgs(ConvertTextView(), subjectBuffer),
                    lastHandler: executeNextCommandTarget);
            }

            var enabled = commandState.IsAvailable ? OLECMDF.OLECMDF_ENABLED : OLECMDF.OLECMDF_INVISIBLE;
            var latched = commandState.IsChecked ? OLECMDF.OLECMDF_LATCHED : OLECMDF.OLECMDF_NINCHED;

            prgCmds[0].cmdf = (uint) (enabled | latched | OLECMDF.OLECMDF_SUPPORTED);

            if (!string.IsNullOrEmpty(commandState.DisplayText) && GetText(commandText) != commandState.DisplayText)
            {
                SetText(commandText, commandState.DisplayText);
            }

            return result;
        }

        private int QueryShowMemberListStatus(OLECMD[] prgCmds)
        {
            prgCmds[0].cmdf = (uint) (OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
            return VSConstants.S_OK;
        }

        private int QueryCompleteWordStatus(OLECMD[] prgCmds)
        {
            prgCmds[0].cmdf = (uint) (OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
            return VSConstants.S_OK;
        }

        private int QueryNavigateHighlightedReferenceStatus(OLECMD[] prgCmds)
        {
            prgCmds[0].cmdf = (uint) (OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
            return VSConstants.S_OK;
        }

        private int QueryUncommentBlockStatus(ref Guid pguidCmdGroup, uint commandCount, OLECMD[] prgCmds, IntPtr commandText)
        {
            return GetCommandState(
                (v, b) => new UncommentSelectionCommandArgs(v, b),
                ref pguidCmdGroup, commandCount, prgCmds, commandText);
        }

        private int QueryCommentBlockStatus(ref Guid pguidCmdGroup, uint commandCount, OLECMD[] prgCmds, IntPtr commandText)
        {
            return GetCommandState(
                (v, b) => new CommentSelectionCommandArgs(v, b),
                ref pguidCmdGroup, commandCount, prgCmds, commandText);
        }

        private int QueryToggleConsumeFirstModeStatus(ref Guid pguidCmdGroup, uint commandCount, OLECMD[] prgCmds, IntPtr commandText)
        {
            return GetCommandState(
                (v, b) => new ToggleCompletionModeCommandArgs(v, b),
                ref pguidCmdGroup, commandCount, prgCmds, commandText);
        }

        private int QueryFormatDocumentStatus(OLECMD[] prgCmds)
        {
            prgCmds[0].cmdf = (uint) (OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
            return VSConstants.S_OK;
        }

        private int QueryFormatSelectionStatus(OLECMD[] prgCmds)
        {
            prgCmds[0].cmdf = (uint) (OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
            return VSConstants.S_OK;
        }

        private int QueryGoToDefinitionStatus(OLECMD[] prgCmds)
        {
            prgCmds[0].cmdf = (uint) (OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
            return VSConstants.S_OK;
        }

        private int QueryQuickInfoStatus(OLECMD[] prgCmds)
        {
            prgCmds[0].cmdf = (uint) (OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
            return VSConstants.S_OK;
        }

        private int QueryParameterInfoStatus(OLECMD[] prgCmds)
        {
            prgCmds[0].cmdf = (uint) (OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
            return VSConstants.S_OK;
        }

        private int QueryStartAutomaticOutliningStatus(ref Guid pguidCmdGroup, uint commandCount, OLECMD[] prgCmds, IntPtr commandText)
        {
            return GetCommandState(
                (v, b) => new StartAutomaticOutliningCommandArgs(v, b),
                ref pguidCmdGroup, commandCount, prgCmds, commandText);
        }

        private int QueryOpenFileStatus(ref Guid pguidCmdGroup, uint commandCount, OLECMD[] prgCmds, IntPtr commandText)
        {
            return GetCommandState(
                (v, b) => new OpenFileCommandArgs(v, b),
                ref pguidCmdGroup, commandCount, prgCmds, commandText);
        }

        private static unsafe string GetText(IntPtr pCmdTextInt)
        {
            if (pCmdTextInt == IntPtr.Zero)
            {
                return string.Empty;
            }

            OLECMDTEXT* pText = (OLECMDTEXT*) pCmdTextInt;

            // Punt early if there is no text in the structure.
            if (pText->cwActual == 0)
            {
                return string.Empty;
            }

            return new string((char*) &pText->rgwz, 0, (int) pText->cwActual);
        }

        private static unsafe void SetText(IntPtr pCmdTextInt, string text)
        {
            OLECMDTEXT* pText = (OLECMDTEXT*) pCmdTextInt;

            // If, for some reason, we don't get passed an array, we should just bail
            if (pText->cwBuf == 0)
            {
                return;
            }

            fixed (char* pinnedText = text)
            {
                char* src = pinnedText;
                char* dest = (char*) (&pText->rgwz);

                // Don't copy too much, and make sure to reserve space for the terminator
                int length = Math.Min(text.Length, (int) pText->cwBuf - 1);

                for (int i = 0; i < length; i++)
                {
                    *dest++ = *src++;
                }

                // Add terminating NUL
                *dest = '\0';

                pText->cwActual = (uint) length;
            }
        }
    }
}
