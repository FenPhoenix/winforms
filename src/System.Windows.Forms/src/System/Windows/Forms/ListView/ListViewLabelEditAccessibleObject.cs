﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Windows.Win32.System.Variant;
using Windows.Win32.UI.Accessibility;
using static System.Windows.Forms.ListViewItem.ListViewSubItem;

namespace System.Windows.Forms;

internal unsafe class ListViewLabelEditAccessibleObject : LabelEditAccessibleObject
{
    private const string LIST_VIEW_LABEL_EDIT_AUTOMATION_ID = "1";

    private readonly WeakReference<ListView> _owningListView;
    private readonly WeakReference<ListViewLabelEditNativeWindow> _labelEdit;
    private readonly LabelEditUiaTextProvider _textProvider;

    public ListViewLabelEditAccessibleObject(ListView owningListView, ListViewLabelEditNativeWindow labelEdit) : base(owningListView, labelEdit)
    {
        ArgumentNullException.ThrowIfNull(owningListView);

        _owningListView = new(owningListView);
        _labelEdit = new(labelEdit);
        UseStdAccessibleObjects(labelEdit.Handle);
        _textProvider = new(owningListView, labelEdit, this);
    }

    private ListViewSubItemAccessibleObject? OwningSubItemAccessibleObject =>
        _owningListView.TryGetTarget(out ListView? target)
            ? target._listViewSubItem?.AccessibilityObject as ListViewSubItemAccessibleObject
            : null;

    private AccessibleObject? OwningListViewItemAccessibleObject =>
        _owningListView.TryGetTarget(out ListView? target)
            ? target._selectedItem?.AccessibilityObject
            : null;

    private protected override string AutomationId => LIST_VIEW_LABEL_EDIT_AUTOMATION_ID;

    public override AccessibleObject? Parent =>
        _owningListView.TryGetTarget(out ListView? target)
        ? target._listViewSubItem is null
            ? OwningListViewItemAccessibleObject
            : target.View == View.Tile
                ? OwningListViewItemAccessibleObject
                : OwningSubItemAccessibleObject
        : null;

    internal override VARIANT GetPropertyValue(UIA_PROPERTY_ID propertyID) =>
        propertyID switch
        {
            UIA_PROPERTY_ID.UIA_IsEnabledPropertyId => _owningListView.TryGetTarget(out ListView? target) ? (VARIANT)target.Enabled : VARIANT.False,
            _ => base.GetPropertyValue(propertyID)
        };

    internal override IRawElementProviderFragment.Interface? FragmentNavigate(NavigateDirection direction) =>
        direction switch
        {
            NavigateDirection.NavigateDirection_NextSibling
                => _owningListView.TryGetTarget(out ListView? target) && target.View == View.Tile ? target._selectedItem?.SubItems[1].AccessibilityObject : null,
            _ => base.FragmentNavigate(direction)
        };

    internal override IRawElementProviderFragmentRoot.Interface? FragmentRoot =>
        _owningListView.TryGetTarget(out ListView? target)
            ? target.AccessibilityObject
            : null;
}
