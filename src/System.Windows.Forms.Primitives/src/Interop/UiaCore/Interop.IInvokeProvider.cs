﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

internal static partial class Interop
{
    internal static partial class UiaCore
    {
        /// <summary>
        ///  Implemented by objects that have a single, unambiguous, action associated with them.
        ///  These objects are usually stateless, and invoking them does not change their own state,
        ///  but causes something to happen in the larger context of the app the control is in.
        ///
        ///  Examples of UI that implements this includes:
        ///  Push buttons
        ///  Hyperlinks
        ///  Menu items
        /// </summary>
        [ComImport]
        [Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IInvokeProvider
        {
            /// <summary>
            ///  Request that the control initiate its action.
            ///  Should return immediately without blocking.
            ///  There is no way to determine what happened, when it happened, or whether
            ///  anything happened at all.
            /// </summary>
            void Invoke();
        }
    }
}
