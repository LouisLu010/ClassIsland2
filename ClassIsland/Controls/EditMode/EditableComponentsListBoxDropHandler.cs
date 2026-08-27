using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactions.DragAndDrop;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Assists;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Helpers.UI;
using ClassIsland.Core.Models.Components;
using ClassIsland.Core.Models.UI;
using ClassIsland.Platforms.Abstraction;
using ClassIsland.Platforms.Abstraction.Models;
using ClassIsland.Services;
using ClassIsland.Shared;
using ClassIsland.Shared.Helpers;
using DynamicData;

namespace ClassIsland.Controls.EditMode;

public class EditableComponentsListBoxDropHandler : DropHandlerBase
{
    private static (int index, bool found) GetTargetIndex(ListBox listBox, DragEventArgs e, IList<ComponentSettings> items, ListBoxItem? explicitTarget)
    {
        var pos = e.GetPosition(listBox);
        if (listBox.GetVisualAt(pos) is Control targetControl
            && targetControl.FindAncestorOfType<ListBoxItem>() is {} listBoxItem
            && listBoxItem.DataContext is ComponentSettings targetItem)
        {
            var rPos = e.GetPosition(listBoxItem);
            // Console.WriteLine($"Pos {rPos.X} of width {listBoxItem.Bounds.Width}");
            var index = items.IndexOf(targetItem);
            if (index >= 0)
            {
                var isBefore = rPos.X <= listBoxItem.Bounds.Width / 2;
                ComponentDragAssist.UpdateIndicator(listBox, listBoxItem, isBefore);
                return (isBefore ? index - 1 : index, true);
            }
        }

        // 没有命中任何项时插到末尾，把指示线画在最后一项右侧。
        ComponentDragAssist.UpdateIndicator(
            listBox, listBox.GetRealizedContainers().OfType<ListBoxItem>().LastOrDefault(), false);
        return (items.Count > 0 ? items.Count - 1 : -1, items.Count > 0);
    }
    
    private bool ValidateCore(EditableComponentsListBox listBox, DragEventArgs e, object? sourceContext, object? targetContext, bool execute, ListBoxItem? listBoxItem)
    {
        e.Handled = true;
        if (sourceContext is ComponentInfo info)
        {
            return ValidateCoreComponentInfo(listBox, e, info, targetContext, execute, listBoxItem);
        }
        if (sourceContext is not EditableComponentsListBoxDragData data || listBox.ItemsSource is not IList<ComponentSettings> targetList)
        {
            return false;
        }
        
        if (data.ComponentSettings == null && data.ComponentInfo == null)
            return false;
        if (data.ComponentSettings is {} settings1 && listBox.ContainerComponentStack.Contains(settings1))
        {
            if (execute)
            {
                listBox.ShowWarningToast("容器组件不能包含自己。");
            }
            return false;
        }
        
        var (targetIndex, foundTargetIndex) = GetTargetIndex(listBox, e, targetList, listBoxItem);
        var insertIndex = foundTargetIndex ? targetIndex + 1 : targetList.Count;

        switch (e.DragEffects)
        {
            case DragDropEffects.Copy when data.ComponentSettings is {} settings:
            {
                if (execute)
                {
                    var clone = ConfigureFileHelper.CopyObject(settings);
                    InsertItem(targetList, clone, insertIndex);
                    listBox.SelectedItem = clone;
                }
                return true;
            }
            case DragDropEffects.Move when data is { ComponentSettings: {} settings, SourceList: { } sourceItems }:
            {
                if (execute)
                {
                    var sourceIndex = sourceItems.IndexOf(settings);
                    if (sourceIndex < 0)
                    {
                        return false;
                    }
                    
                    if (ReferenceEquals(sourceItems, targetList))
                    {
                        var moveIndex = foundTargetIndex ? targetIndex : targetList.Count - 1;
                        var newIndex = sourceIndex > moveIndex ? moveIndex + 1 : moveIndex;
                        Console.WriteLine($"ti={targetIndex}, ni={newIndex}");
                        MoveItem(targetList, sourceIndex, Math.Clamp(newIndex, 0, targetList.Count - 1));
                    }
                    else
                    {
                        MoveItem(sourceItems, targetList, sourceIndex, insertIndex);
                    }
                    listBox.SelectedItem = settings;
                }
                return true;
            }
            case DragDropEffects.None:
            case DragDropEffects.Link:
            default:
                return false;
        }

        return false;
    }
        
    private bool ValidateCoreComponentInfo(EditableComponentsListBox listBox, DragEventArgs e, ComponentInfo data, object? targetContext, bool execute, ListBoxItem? listBoxItem)
    {
        if (listBox.ItemsSource is not IList<ComponentSettings> targetList)
        {
            return false;
        }
        
        var (targetIndex, foundTargetIndex) = GetTargetIndex(listBox, e, targetList, listBoxItem);
        var insertIndex = foundTargetIndex ? targetIndex + 1 : targetList.Count;

        if (execute)
        {
            var componentSettings = new ComponentSettings()
            {
                Id = data.Guid.ToString()
            };
            ComponentsService.LoadComponentSettings(componentSettings,
                componentSettings.AssociatedComponentInfo.ComponentType!.BaseType!);
            InsertItem(targetList, componentSettings, insertIndex);
            if (data.SettingsType != null)
            {
                IAppHost.GetService<ITutorialService>().BeginNotCompletedTutorials(
                    "classisland.getStarted.componentsEditing/componentSettings");
            } else if (data.IsComponentContainer)
            {
                IAppHost.GetService<ITutorialService>().BeginNotCompletedTutorials(
                    "classisland.getStarted.componentsEditing/containerComponent");
            }
            listBox.SelectedItem = componentSettings;
        }
        return true;

    }
    
    /// <summary>
    /// 拖动经过时持续刷新插入位置指示线。
    /// </summary>
    /// <remarks>
    /// Execute 要到放下那一刻才调用，不足以在拖动过程中给出反馈，因此在这里更新。
    /// 这里只调 GetTargetIndex 算落点，不走 ValidateCore——后者开头会设 e.Handled = true，
    /// 在 Over 阶段那样做会阻断事件冒泡。
    /// </remarks>
    public override void Over(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
    {
        if (sender is EditableComponentsListBox { ItemsSource: IList<ComponentSettings> items } listBox)
        {
            GetTargetIndex(listBox, e, items, null);
        }
        base.Over(sender, e, sourceContext, targetContext);
    }

    public override bool Validate(object? sender, DragEventArgs e, object? sourceContext, object? targetContext, object? state)
    {
        if (e.Handled)
        {
            return false;
        }
        return sender switch
        {
            EditableComponentsListBox listBox => ValidateCore(listBox, e, sourceContext, targetContext, false, null),
            // ListBoxItem listBoxItem when listBoxItem.FindAncestorOfType<EditableComponentsListBox>() is { } owner =>
            //     ValidateCore(owner, e, sourceContext, targetContext, false, listBoxItem),
            _ => false
        };
    }

    public override bool Execute(object? sender, DragEventArgs e, object? sourceContext, object? targetContext, object? state)
    {
        if (e.Handled)
        {
            return false;
        }
        try
        {
            return sender switch
            {
                EditableComponentsListBox listBox => ValidateCore(listBox, e, sourceContext, targetContext, true, null),
                // ListBoxItem listBoxItem when listBoxItem.FindAncestorOfType<EditableComponentsListBox>() is { } owner =>
                //     ValidateCore(owner, e, sourceContext, targetContext, true, listBoxItem),
                _ => false
            };
        }
        finally
        {
            // 放下后务必清除，否则指示线会残留在最后经过的项上。
            ComponentDragAssist.ClearIndicator(sender);
        }
    }

    public override void Cancel(object? sender, RoutedEventArgs e)
    {
        ComponentDragAssist.ClearIndicator(sender);
        base.Cancel(sender, e);
    }

    public override void Leave(object? sender, RoutedEventArgs e)
    {
        ComponentDragAssist.ClearIndicator(sender);
        base.Leave(sender, e);
    }
}