using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace ClassIsland.Core.Assists;

/// <summary>
/// 拖放时组件将被插入到目标项的哪一侧。
/// </summary>
public enum ComponentDropSide
{
    /// <summary>
    /// 不显示插入位置指示。
    /// </summary>
    None,

    /// <summary>
    /// 插入到目标项之前。
    /// </summary>
    Before,

    /// <summary>
    /// 插入到目标项之后。
    /// </summary>
    After
}

/// <summary>
/// 组件拖放的插入位置指示助手。
/// </summary>
/// <remarks>
/// 原先判断落点全靠跟随光标的拖动预览，而预览由浮动 <see cref="Window"/> 承载，
/// 在浏览器端不可用（无法构造 Window），导致拖动时完全看不出会插到哪里。
/// 这里用一个附加属性标记当前落点，由列表项模板画出指示线。
/// </remarks>
public static class ComponentDragAssist
{
    public static readonly AttachedProperty<ComponentDropSide> DropSideProperty =
        AvaloniaProperty.RegisterAttached<ListBoxItem, ComponentDropSide>(
            "DropSide", typeof(ComponentDragAssist));

    public static void SetDropSide(ListBoxItem obj, ComponentDropSide value) => obj.SetValue(DropSideProperty, value);
    public static ComponentDropSide GetDropSide(ListBoxItem obj) => obj.GetValue(DropSideProperty);

    /// <summary>用于「插入到此项之前」指示线的可见性绑定。</summary>
    public static readonly FuncValueConverter<ComponentDropSide, bool> IsDropBeforeConverter =
        new(value => value == ComponentDropSide.Before);

    /// <summary>用于「插入到此项之后」指示线的可见性绑定。</summary>
    public static readonly FuncValueConverter<ComponentDropSide, bool> IsDropAfterConverter =
        new(value => value == ComponentDropSide.After);

    /// <summary>
    /// 只在 <paramref name="target"/> 上标注落点，其余项清除。
    /// </summary>
    public static void UpdateIndicator(ListBox listBox, ListBoxItem? target, bool isBefore)
    {
        var side = isBefore ? ComponentDropSide.Before : ComponentDropSide.After;
        foreach (var item in listBox.GetRealizedContainers())
        {
            if (item is not ListBoxItem listBoxItem)
            {
                continue;
            }
            var expected = ReferenceEquals(listBoxItem, target) ? side : ComponentDropSide.None;
            if (GetDropSide(listBoxItem) != expected)
            {
                SetDropSide(listBoxItem, expected);
            }
        }
    }

    /// <summary>
    /// 清除列表内所有项的插入位置指示。拖动结束、取消或离开时必须调用，否则指示线会残留。
    /// </summary>
    public static void ClearIndicator(object? listBox)
    {
        if (listBox is not ListBox lb)
        {
            return;
        }
        foreach (var item in lb.GetRealizedContainers())
        {
            if (item is ListBoxItem listBoxItem && GetDropSide(listBoxItem) != ComponentDropSide.None)
            {
                SetDropSide(listBoxItem, ComponentDropSide.None);
            }
        }
    }
}
