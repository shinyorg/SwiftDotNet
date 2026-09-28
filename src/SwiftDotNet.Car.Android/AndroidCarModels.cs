// androidx.car.app builder bindings are annotated as returning nullable builders/objects but never return null.
#pragma warning disable CS8602, CS8603, CS8604

namespace SwiftDotNet.AndroidAuto;

using Android.Content;
using Android.Graphics;
using AndroidX.Car.App.Model;
using AndroidX.Core.Graphics.Drawable;
using CarAppAction = AndroidX.Car.App.Model.Action;

/// <summary>Leaf conversions (icons, colors, actions, rows) from SwiftDotNet.Car models to androidx.car.app.model.</summary>
static class AndroidCarModels
{
    public static CarColor ToCarColor(this SwiftColor color)
    {
        var argb = unchecked((int)CarColors.Resolve(color).ToArgb());
        return CarColor.CreateCustom(argb, argb);
    }

    public static CarIcon? ToCarIcon(this CarImage? image, Context context)
    {
        if (image is null)
            return null;

        var icon = image switch
        {
            CarSymbolImage s => FromResource(context, s.AndroidResourceName),
            CarResourceImage r => FromResource(context, r.Name),
            CarBytesImage b => BitmapFactory.DecodeByteArray(b.Data, 0, b.Data.Length) is { } bitmap ? IconCompat.CreateWithBitmap(bitmap) : null,
            _ => null
        };
        // keep layouts stable when an icon is missing rather than failing the whole template
        icon ??= IconCompat.CreateWithResource(context, global::Android.Resource.Drawable.IcMenuInfoDetails);

        var builder = new CarIcon.Builder(icon);
        if (image.Tint is { } tint)
            builder.SetTint(tint.ToCarColor());
        return builder.Build();
    }

    static IconCompat? FromResource(Context context, string name)
    {
        var resources = context.Resources!;
        var id = resources.GetIdentifier(name, "drawable", context.PackageName);
        if (id == 0)
            id = resources.GetIdentifier(name, "mipmap", context.PackageName);
        return id == 0 ? null : IconCompat.CreateWithResource(context, id);
    }

    public static CarAppAction ToAction(this CarAction action, Context context, bool iconOnly = false)
    {
        var builder = new CarAppAction.Builder();
        if (!iconOnly || action.Image is null)
            builder.SetTitle(action.Title);
        if (action.Image.ToCarIcon(context) is { } icon)
            builder.SetIcon(icon);
        builder.SetOnClickListener(new ClickListener(action.OnSelect));

        switch (action.Style)
        {
            case CarActionStyle.Primary:
                builder.SetFlags(CarAppAction.FlagPrimary);
                break;
            case CarActionStyle.Destructive:
                builder.SetBackgroundColor(CarColor.Red);
                break;
        }
        if (!action.IsEnabled)
            builder.SetEnabled(false);
        return builder.Build();
    }

    public static ActionStrip? ToActionStrip(this IReadOnlyList<CarAction> actions, Context context, bool iconOnly = false)
    {
        if (actions.Count == 0)
            return null;
        var builder = new ActionStrip.Builder();
        foreach (var a in actions)
            builder.AddAction(a.ToAction(context, iconOnly));
        return builder.Build();
    }

    public static Row ToRow(this CarListItemBase item, Context context)
    {
        var builder = new Row.Builder().SetTitle(item.Title);
        if (!string.IsNullOrEmpty(item.Detail))
            builder.AddText(item.Detail);
        if (item.Image.ToCarIcon(context) is { } icon)
            builder.SetImage(icon, Row.ImageTypeSmall);

        switch (item)
        {
            case CarToggleItem toggle:
                builder.SetToggle(new Toggle.Builder(new CheckedListener(toggle.OnToggled)).SetChecked(toggle.IsOn).Build());
                break;
            case CarListItem row when row.OnSelect is not null:
                builder.SetOnClickListener(new ClickListener(row.OnSelect));
                if (row.Accessory == CarAccessory.Disclosure)
                    builder.SetBrowsable(true);
                break;
        }

        if (!item.IsEnabled)
            builder.SetEnabled(false);
        return builder.Build();
    }

    public static ItemList ToItemList(this IEnumerable<CarListItemBase> items, Context context, string? emptyMessage = null)
    {
        var builder = new ItemList.Builder();
        var any = false;
        foreach (var item in items)
        {
            builder.AddItem(item.ToRow(context));
            any = true;
        }
        if (!any)
            builder.SetNoItemsMessage(emptyMessage ?? "No items");
        return builder.Build();
    }
}

internal sealed class ClickListener(System.Action? onClick) : Java.Lang.Object, IOnClickListener
{
    public void OnClick() => onClick?.Invoke();
}

internal sealed class CheckedListener(Action<bool>? onChanged) : Java.Lang.Object, Toggle.IOnCheckedChangeListener
{
    public void OnCheckedChange(bool isChecked) => onChanged?.Invoke(isChecked);
}
