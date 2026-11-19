# Space Engineers API dump

Generated: 2026-11-18 22:48  
Source assemblies: `H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`  
Types: 8

## `Sandbox.ModAPI.MyModScreen`

- assembly: `Sandbox.Game`
- kind: class abstract
- base: `System.Object`

### Properties
```csharp
public String Name { get; }
public Boolean IsOpen { get; }
public Boolean HasFocus { get; }
public Boolean IsVisible { get; }
public Vector2 Position { get; set; }
public Nullable<Vector2> Size { get; set; }
public Nullable<Vector4> BackgroundColor { get; set; }
public Boolean CloseOnEscape { get; set; }
public Boolean CloseButtonEnabled { get; set; }
public Boolean DrawMouseCursor { get; set; }
public Boolean EnabledBackgroundFade { get; set; }
public Boolean SkipTransition { get; set; }
public Boolean PausesGameInSinglePlayer { get; set; }
```

### Methods
```csharp
public Void Close()
public Void CloseNow()
public Void Hide()
public Void Open()
public Void Unhide()
```

## `Sandbox.ModAPI.MyScreenSpace`

- assembly: `Sandbox.Game`
- kind: class abstract sealed
- base: `System.Object`

### Methods
```csharp
public static RectangleF GetSafeGuiArea()
public static Vector2 NormalizedSizeToPixels(Vector2 normalizedSize)
public static Vector2 NormalizedToPixels(Vector2 normalizedPosition)
public static Vector2 PixelSizeToNormalized(Vector2 pixelSize)
public static Vector2 PixelsToNormalized(Vector2 pixelPosition)
public static Boolean TryGetCursorPosition(out ref Vector2 normalizedPosition)
public static Boolean TryGetCursorPositionPixels(out ref Vector2 pixelPosition)
public static MyScissorScope UsingScissorRectangle(RectangleF normalizedRectangle, MyScreenSpaceLayer layer = BelowHud)
public static MyScissorScope UsingScissorRectanglePixels(Rectangle pixelRectangle, MyScreenSpaceLayer layer = BelowHud)
```

## `Sandbox.ModAPI.MyScreenSpaceLayer`

- assembly: `Sandbox.Common`
- kind: enum 
- base: `System.Enum`
- interfaces: `System.IComparable`, `System.ISpanFormattable`, `System.IFormattable`, `System.IConvertible`
- values: `BelowHud`, `AboveHud`, `AboveEverything`

## `Sandbox.ModAPI.MyScreenSpaceSprite`

- assembly: `Sandbox.Game`
- kind: class abstract sealed
- base: `System.Object`

### Methods
```csharp
public static Void Draw(MyStringId material, Vector2 normalizedPosition, Vector2 normalizedSize, Color color, MyGuiDrawAlignEnum align = HORISONTAL_LEFT_AND_VERTICAL_TOP, Single rotation = 0, MyScreenSpaceLayer layer = BelowHud)
public static Void DrawPivoted(MyStringId material, Rectangle destination, Nullable<Rectangle> source, Color color, Single rotation, Vector2 pivotPixels, MyScreenSpaceLayer layer = BelowHud)
public static Void DrawPixels(MyStringId material, Rectangle destination, Nullable<Rectangle> source, Color color, Single rotation = 0, MyScreenSpaceLayer layer = BelowHud)
public static Vector2 GetTextureSize(MyStringId material)
public static Boolean IsMaterialAvailable(MyStringId material)
```

## `Sandbox.ModAPI.MyScreenSpaceText`

- assembly: `Sandbox.Game`
- kind: class abstract sealed
- base: `System.Object`

### Methods
```csharp
public static Void Draw(String font, String text, Vector2 normalizedPosition, Single scale, Color color, MyGuiDrawAlignEnum align = HORISONTAL_LEFT_AND_VERTICAL_TOP, Single maxWidth = Infinity, MyScreenSpaceLayer layer = BelowHud)
public static Void DrawPixels(String font, String text, Vector2 pixelPosition, Single scale, Color color, MyGuiDrawAlignEnum align = HORISONTAL_LEFT_AND_VERTICAL_TOP, Single maxWidth = Infinity, MyScreenSpaceLayer layer = BelowHud)
public static Int32 GetCharsThatFit(String font, StringBuilder text, Single scale, Single maxWidth)
public static Single GetLineHeight(String font, Single scale)
public static Boolean IsFontAvailable(String font)
public static Vector2 Measure(String font, StringBuilder text, Single scale)
public static Vector2 Measure(String font, String text, Single scale)
public static Vector2 MeasurePixels(String font, StringBuilder text, Single scale)
```

## `VRage.Game.ModAPI.IMyGeneratedTexture`

- assembly: `VRage.Game`
- kind: interface 
- interfaces: `System.IDisposable`

### Properties
```csharp
public String Name { get; }
public MyStringId MaterialId { get; }
public Vector2I Size { get; }
public MyGeneratedTextureFormat Format { get; }
public Int32 ByteCount { get; }
public Boolean IsValid { get; }
```

### Methods
```csharp
public abstract Byte[] BeginUpdate()
public abstract Void EndUpdate(Byte[] pixels)
```

## `VRage.Game.ModAPI.IMyGeneratedTextures`

- assembly: `VRage.Game`
- kind: interface 

### Properties
```csharp
public Boolean IsSupported { get; }
public Int32 MaxTextureSide { get; }
```

### Methods
```csharp
public abstract IMyGeneratedTexture Create(String nameHint, Vector2I size, MyGeneratedTextureFormat format = Srgb)
```

## `VRage.Game.ModAPI.MyGeneratedTextureFormat`

- assembly: `VRage.Game`
- kind: enum 
- base: `System.Enum`
- interfaces: `System.IComparable`, `System.ISpanFormattable`, `System.IFormattable`, `System.IConvertible`
- values: `Srgb`, `Linear`


