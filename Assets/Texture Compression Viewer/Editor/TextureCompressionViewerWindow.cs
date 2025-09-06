using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

public class TextureCompressionViewerWindow : EditorWindow
{
    private Texture2D sourceTexture;

    private Texture2D previewA;
    private Texture2D previewB;

    private string tempFolderName = "Temp";
    private string tempFolderParent = "Assets/Texture Compression Viewer";
    private string tempFolder = "Assets/Texture Compression Viewer/Temp";
    private string tempAPath;
    private string tempBPath;
    
    private const string PlatformName = "Android";

    public TextureImporterFormat formatA = TextureImporterFormat.Automatic;
    public int compressionQualityA = 50;
    public int maxSizeA = 1024;

    public TextureImporterFormat formatB = TextureImporterFormat.Automatic;
    public int compressionQualityB = 50;
    public int maxSizeB = 1024;

    private bool autoUpdate = true;
    private float handlePos = 0.5f;
    private bool dragging = false;

    private static readonly List<TextureImporterFormat> CompressionFormats = new()
    {
        TextureImporterFormat.Automatic,
        TextureImporterFormat.RGBA32,
        TextureImporterFormat.RGBA16,
        TextureImporterFormat.RGB24,
        TextureImporterFormat.RGB16,
        TextureImporterFormat.Alpha8,
        TextureImporterFormat.ETC_RGB4,
        TextureImporterFormat.ETC_RGB4Crunched,
        TextureImporterFormat.ETC2_RGB4,
        TextureImporterFormat.ETC2_RGBA8,
        TextureImporterFormat.ETC2_RGBA8Crunched,
        TextureImporterFormat.ETC2_RGB4_PUNCHTHROUGH_ALPHA,
        TextureImporterFormat.ASTC_4x4,
        TextureImporterFormat.ASTC_5x5,
        TextureImporterFormat.ASTC_6x6,
        TextureImporterFormat.ASTC_8x8,
        TextureImporterFormat.ASTC_10x10,
        TextureImporterFormat.ASTC_12x12,
        TextureImporterFormat.ASTC_HDR_4x4,
        TextureImporterFormat.ASTC_HDR_5x5,
        TextureImporterFormat.ASTC_HDR_6x6,
        TextureImporterFormat.ASTC_HDR_8x8,
        TextureImporterFormat.ASTC_HDR_10x10,
        TextureImporterFormat.ASTC_HDR_12x12,
    };
    
    private List<int> sizeOptions = new List<int> { 32, 64, 128, 256, 512, 1024, 2048, 4096 };

    // UI elements
    private ObjectField sourceField;
    private PopupField<TextureImporterFormat> formatAField;
    private PopupField<TextureImporterFormat> formatBField;
    private SliderInt compressionQualityA_Slider;
    private SliderInt compressionQualityB_Slider;
    private PopupField<int> maxSizeAField;
    private PopupField<int> maxSizeBField;
    private Toggle autoUpdateToggle;
    private Button updateButton;
    private Label compressPrefWarning;
    private IMGUIContainer previewContainer;
    private Label textureStatsALabel;
    private Label textureStatsBLabel;
    
    private string[] compressPrefKeys = { "kCompressTexturesOnImport", "CompressTexturesOnImport", "CompressAssetsOnImport" };
    private Dictionary<string, bool> prevImportCompressSetKeys = new();

    [Flags]
    private enum PreviewUpdateMode
    {
        None = 0,
        A = 1 << 0,
        B = 1 << 1
    }

    private PreviewUpdateMode previewUpdateMode = PreviewUpdateMode.A | PreviewUpdateMode.B;

    [MenuItem("Window/Analysis/Texture Compression Preview")]
    public static void ShowWindow()
    {
        var w = GetWindow<TextureCompressionViewerWindow>("Texture Compression Viewer");
        w.minSize = new Vector2(600, 360);
    }

    // --- New: project-window context menu to open this preview on a selected texture ---
    [MenuItem("Assets/Texture Compression Preview", false, 1000)]
    private static void OpenFromProjectMenu()
    {
        var tex = Selection.activeObject as Texture2D;
        if (tex == null)
        {
            return;
        }
        
        var w = GetWindow<TextureCompressionViewerWindow>("Texture Compression Viewer");
        w.minSize = new Vector2(600, 360);
        w.SetSourceTextureFromAsset(tex);
    }

    [MenuItem("Assets/Texture Compression Viewer", true)]
    private static bool OpenFromProjectMenuValidation()
    {
        return Selection.activeObject is Texture2D;
    }

    private void OnEnable()
    {
        CleanupPreviewFiles();

        if (!AssetDatabase.IsValidFolder(tempFolder))
        {
            Directory.CreateDirectory(tempFolder);
            AssetDatabase.Refresh();
        }
        
        BuildUI();
    }

    private void OnDisable()
    {
        CleanupPreviewFiles();
    }

    private void BuildUI()
    {
        rootVisualElement.Clear();
        
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            compressPrefWarning = new Label
            {
                text = "Only the Android build target is supported.",
                style =
                {
                    marginTop = 6,
                    paddingLeft = 16,
                    whiteSpace = WhiteSpace.Normal,
                    display = DisplayStyle.Flex,
                    color = new StyleColor(new Color(1.0f, 0.85f, 0.2f)),
                }
            };
            rootVisualElement.Add(compressPrefWarning);
        }

        // Main content split
        var content = new VisualElement { style =
            {
                flexDirection = FlexDirection.Row, marginTop = 8,
                flexGrow = 1
            }
        };

        // Left panel for settings
        var left = new ScrollView(ScrollViewMode.Vertical) { style = { width = 320, marginRight = 8 } };

        sourceField = new ObjectField() { objectType = typeof(Texture2D) };
        sourceField.RegisterValueChangedCallback(changeEvent =>
        {
            SetSourceTextureFromAsset(sourceField.value as Texture2D);

            if (autoUpdate)
            {
                GeneratePreview();
            } 
        });
        left.Add(CreateLabeledRow("Source Texture", sourceField));

        textureStatsALabel = new Label("Left")
            { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 24, paddingLeft = 16 } };
        left.Add(textureStatsALabel);

        maxSizeAField = new PopupField<int>(sizeOptions, sizeOptions.IndexOf(maxSizeA));
        maxSizeAField.RegisterValueChangedCallback(evt =>
        {
            maxSizeA = evt.newValue;
            previewUpdateMode |= PreviewUpdateMode.A;
            if (autoUpdate)
            {
                GeneratePreview();
            }
        });
        left.Add(CreateLabeledRow("Max Size", maxSizeAField));
        
        formatAField = new PopupField<TextureImporterFormat>(CompressionFormats, CompressionFormats.IndexOf(formatA));
        formatAField.RegisterValueChangedCallback(evt => { 
            previewUpdateMode |= PreviewUpdateMode.A;
            formatA = evt.newValue;
            if (autoUpdate)
            {
                GeneratePreview();
            }
        });
        left.Add(CreateLabeledRow("Format", formatAField));
        
        compressionQualityA_Slider = new SliderInt(0, 100) { lowValue = 0, highValue = 100, value = compressionQualityA };
        int pendingCompressionA = compressionQualityA;

        compressionQualityA_Slider.RegisterValueChangedCallback(evt =>
        {
            pendingCompressionA = evt.newValue;
        });
        
        compressionQualityA_Slider.RegisterCallback<PointerCaptureOutEvent>(evt =>
        {
            compressionQualityA = pendingCompressionA;
            previewUpdateMode |= PreviewUpdateMode.A;

            if (autoUpdate)
            {
                GeneratePreview();
            }
        });
        left.Add(CreateLabeledRow("Compression Quality", compressionQualityA_Slider));

        textureStatsBLabel = new Label("Right")
            { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 30, paddingLeft = 16 } };
        left.Add(textureStatsBLabel);
        
        maxSizeBField = new PopupField<int>(sizeOptions, sizeOptions.IndexOf(maxSizeB));
        maxSizeBField.RegisterValueChangedCallback(evt =>
        {
            maxSizeB = evt.newValue;
            previewUpdateMode |= PreviewUpdateMode.B;
            if (autoUpdate)
            {
                GeneratePreview();
            }
        });
        left.Add(CreateLabeledRow("Max Size", maxSizeBField));
        
        formatBField = new PopupField<TextureImporterFormat>(CompressionFormats, CompressionFormats.IndexOf(formatB));
        formatBField.RegisterValueChangedCallback(evt =>
        {
            previewUpdateMode |= PreviewUpdateMode.B;
            formatB = evt.newValue;

            if (autoUpdate)
            {
                GeneratePreview();
            }
        });
        left.Add(CreateLabeledRow("Format", formatBField));
        
        compressionQualityB_Slider = new SliderInt(0, 100) { lowValue = 0, highValue = 100, value = compressionQualityB };
        int pendingCompressionB = compressionQualityB;

        compressionQualityB_Slider.RegisterValueChangedCallback(evt =>
        {
            pendingCompressionB = evt.newValue;
        });
        
        compressionQualityB_Slider.RegisterCallback<PointerCaptureOutEvent>(evt =>
        {
            compressionQualityB = pendingCompressionB;
            previewUpdateMode |= PreviewUpdateMode.B;
            if (autoUpdate)
            {
                GeneratePreview();
            }
        });
        left.Add(CreateLabeledRow("Compression Quality", compressionQualityB_Slider));
        
        left.Add(CreateLabeledRow("", null));
        
        autoUpdateToggle = new Toggle { value = autoUpdate };
        autoUpdateToggle.RegisterValueChangedCallback(evt =>
        {
            autoUpdate = evt.newValue;
            if (autoUpdate)
            {
                GeneratePreview();
            }
        });
        
        left.Add(CreateLabeledRow("Auto Update Preview", autoUpdateToggle));
        
        updateButton = new Button(() =>
        {
            previewUpdateMode = PreviewUpdateMode.A | PreviewUpdateMode.B;
            GeneratePreview();
        })
        {
            text = "Update Preview",
            style =
            {
                marginLeft = 16
            }
        };
        left.Add(updateButton);

        content.Add(left);

        // Right panel: preview
        var right = new VisualElement { style = { flexGrow = 1 } };
        previewContainer = new IMGUIContainer(() => { DrawPreviewIMGUI(); });
        previewContainer.style.flexGrow = 1;
        previewContainer.RegisterCallback<PointerDownEvent>(OnPointerDownPreview);
        previewContainer.RegisterCallback<PointerMoveEvent>(OnPointerMovePreview);
        previewContainer.RegisterCallback<PointerUpEvent>(OnPointerUpPreview);

        right.Add(previewContainer);
        content.Add(right);

        rootVisualElement.Add(content);
    }

    private void OnPointerDownPreview(PointerDownEvent evt)
    {
        var rect = previewContainer.contentRect;
        float localX = evt.localPosition.x;
        float w = rect.width;
        float handleX = w * handlePos;
        Rect handleRect = new Rect(handleX - 6, 0, 12, rect.height);
        
        if (handleRect.Contains(new Vector2(localX, evt.localPosition.y)))
        {
            dragging = true;
            evt.StopImmediatePropagation();
        }
        else if (rect.Contains(evt.localPosition))
        {
            handlePos = Mathf.Clamp01(localX / w);
            previewContainer.MarkDirtyRepaint();
            evt.StopImmediatePropagation();
        }
    }

    private void OnPointerMovePreview(PointerMoveEvent evt)
    {
        if (!dragging)
        {
            return;
        }
        
        var rect = previewContainer.contentRect;
        float localX = evt.localPosition.x;
        float w = rect.width;
        handlePos = Mathf.Clamp01(localX / w);
        previewContainer.MarkDirtyRepaint();
        evt.StopImmediatePropagation();
    }

    private void OnPointerUpPreview(PointerUpEvent evt)
    {
        dragging = false;
    }

    private void DrawPreviewIMGUI()
    {
        var rect = GUILayoutUtility.GetRect(previewContainer.contentRect.width, previewContainer.contentRect.height);
        if (previewA == null || previewB == null)
        {
            GUI.Label(new Rect(rect.x + 8, rect.y + 8, 600, 20), "No preview available." );
            return;
        }

        DrawSplitPreview(rect, previewA, previewB, handlePos);
    }

    private void DrawSplitPreview(Rect rect, Texture2D leftTex, Texture2D rightTex, float handle)
    {
        float handleX = rect.x + rect.width * handle;
        float h = rect.height;
        float w = rect.width;
        float leftWidth = Mathf.Clamp(handleX - rect.x, 0, w);

        // Draw B as full background
        GUI.BeginGroup(rect);
        if (rightTex != null)
        {
            Rect full = new Rect(0, 0, w, h);
            GUI.DrawTexture(full, rightTex, ScaleMode.ScaleToFit);
        }

        // Draw A on top, clipped
        GUI.BeginGroup(new Rect(0, 0, leftWidth, h));
        if (leftTex != null)
        {
            Rect full = new Rect(0, 0, w, h);
            GUI.DrawTexture(full, leftTex, ScaleMode.ScaleToFit);
        }
        GUI.EndGroup();
        GUI.EndGroup();

        // divider
        Handles.BeginGUI();
        Color prev = Handles.color;
        Handles.color = Color.white;
        Handles.DrawLine(new Vector3(handleX, rect.y), new Vector3(handleX, rect.y + h));
        Handles.color = prev;
        Handles.EndGUI();
        
        EditorGUIUtility.AddCursorRect(new Rect(handleX - 6, rect.y, 12, h), MouseCursor.ResizeHorizontal);
    }

    private Texture2D ApplyImporterSettingsAndGetPreviewTexture(string assetPath, TextureImporterFormat format, Int32 maxSize, Int32 quality, out string statText)
    {
        statText = null;
        TextureImporter texImp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (texImp == null)
        {
            return null;
        }
        
        TextureImporterPlatformSettings plat = texImp.GetPlatformTextureSettings(PlatformName);
        plat.name = PlatformName;
        plat.overridden = true;
        plat.format = format;
        plat.maxTextureSize = maxSize;
        plat.compressionQuality = Mathf.Clamp(quality, 0, 100);
        
        texImp.textureType = TextureImporterType.Default;
        texImp.SetPlatformTextureSettings(plat);
        texImp.textureCompression = TextureImporterCompression.Compressed; 
        texImp.SaveAndReimport();
        
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        Texture2D reimported = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (reimported == null)
        {
            Debug.LogWarning("Failed to load reimported texture at " + assetPath); 
            return null;
        }
        
        var textureUtilType = typeof(Editor).Assembly.GetType("UnityEditor.TextureUtil");
        
        MethodInfo getStorageMemorySizeLongMethod = textureUtilType?.GetMethod(
            "GetStorageMemorySizeLong",
            BindingFlags.Static | BindingFlags.Public
        );
        var result = (long)getStorageMemorySizeLongMethod.Invoke(null, new object[] { reimported });
        var formattedBytes = EditorUtility.FormatBytes(result);
        
        MethodInfo getTextureFormatMethod = textureUtilType?.GetMethod(
            "GetTextureFormat",
            BindingFlags.Static | BindingFlags.Public
        );
        var texFormat = (TextureFormat)getTextureFormatMethod.Invoke(null, new[] { reimported });
        
        statText = $"{reimported.width}x{reimported.height} {texFormat.ToString()} {formattedBytes}";
        
        return reimported;
    }

    private void GeneratePreview()
    {
        if (previewUpdateMode == PreviewUpdateMode.None)
        {
            return;
        }
        
        if (sourceTexture == null)
        {
            EditorUtility.DisplayDialog("No source", "Please select a source texture first.", "OK");
            return;
        }
        
        string srcPath = AssetDatabase.GetAssetPath(sourceTexture);
        if (string.IsNullOrEmpty(srcPath))
        {
            EditorUtility.DisplayDialog("Invalid asset", "Selected texture is not a valid asset in the project.", "OK");
            return;
        }
        
        prevImportCompressSetKeys.Clear();
        foreach (var k in compressPrefKeys)
        {
            prevImportCompressSetKeys[k] = (EditorPrefs.HasKey(k)) && EditorPrefs.GetBool(k);
            EditorPrefs.SetBool(k, true);
        }
        
        if (!AssetDatabase.IsValidFolder(tempFolder))
        {
            AssetDatabase.CreateFolder(tempFolderParent, tempFolderName);
        }

        string ext = Path.GetExtension(srcPath);
        tempAPath = tempFolder + "/A" + ext;
        tempBPath = tempFolder + "/B" + ext;
        
        if ((previewUpdateMode & PreviewUpdateMode.A) != 0)
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(tempAPath) != null)
            {
                AssetDatabase.DeleteAsset(tempAPath);
            }
            
            if (!AssetDatabase.CopyAsset(srcPath, tempAPath))
            {
                return;
            }
            
            previewA = ApplyImporterSettingsAndGetPreviewTexture(tempAPath, formatA, maxSizeA, compressionQualityA, out var statsTextA);
            textureStatsALabel.text = $"Left: {statsTextA}";
        }
        
        if ((previewUpdateMode & PreviewUpdateMode.B) != 0)
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(tempBPath) != null)
            {
                AssetDatabase.DeleteAsset(tempBPath);
            }

            if (!AssetDatabase.CopyAsset(srcPath, tempBPath))
            {
                return;
            }

            previewB = ApplyImporterSettingsAndGetPreviewTexture(tempBPath, formatB, maxSizeB, compressionQualityB, out var statsTextB);
            textureStatsBLabel.text = $"Right: {statsTextB}";
        }

        AssetDatabase.Refresh();
        previewContainer.MarkDirtyRepaint();
        previewUpdateMode = PreviewUpdateMode.None;

        foreach (var kv in prevImportCompressSetKeys)
        {
            EditorPrefs.SetBool(kv.Key, kv.Value);
        }
    }

    private void CleanupPreviewFiles()
    {
        var cleanupChangesPresent = false;

        if (!string.IsNullOrEmpty(tempAPath))
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(tempAPath) != null)
            {
                AssetDatabase.DeleteAsset(tempAPath);
                cleanupChangesPresent = true;
            }

            tempAPath = null;
            previewA = null;
        }

        if (!string.IsNullOrEmpty(tempBPath))
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(tempBPath) != null)
            {
                AssetDatabase.DeleteAsset(tempBPath);
                cleanupChangesPresent = true;
            }

            tempBPath = null;
            previewB = null;
        }

        if (AssetDatabase.IsValidFolder(tempFolder))
        {
            AssetDatabase.DeleteAsset(tempFolder);
            cleanupChangesPresent = true;
        }

        if (cleanupChangesPresent)
        {
            AssetDatabase.Refresh();
            previewContainer?.MarkDirtyRepaint();
        }
    }

    private void SetSourceTextureFromAsset(Texture2D tex)
    {
        previewUpdateMode = PreviewUpdateMode.A | PreviewUpdateMode.B;
        
        sourceTexture = tex;
        if (sourceField == null)
        {
            return;
            
        }
        
        sourceField.value = tex;

        // Attempt to read importer settings and use them as initial preview settings
        string path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        
        var texImp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (texImp == null)
        {
            return;
        }
        
        var platformSettings = texImp.GetPlatformTextureSettings(PlatformName);
        if (platformSettings is { overridden: true })
        {
            formatA = formatB = platformSettings.format;
            maxSizeA = maxSizeB = platformSettings.maxTextureSize;
            compressionQualityA = compressionQualityB = platformSettings.compressionQuality;
        }
        else
        {
            maxSizeA = maxSizeB = texImp.maxTextureSize;
            compressionQualityA = compressionQualityB = texImp.compressionQuality;
            formatA = formatB = TextureImporterFormat.Automatic;
        }

        maxSizeAField.value = maxSizeBField.value = maxSizeA;
        compressionQualityA_Slider.value = compressionQualityB_Slider.value = compressionQualityA;
        formatAField.value = formatBField.value = formatA;
    }
    
    private VisualElement CreateLabeledRow(string labelText, VisualElement fieldElement, int labelWidth = 130, float spacing = 4)
    {
        var row = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center,
                marginTop = 2,
                marginBottom = 2,
                paddingLeft = 16,
                paddingTop = 0,
                paddingBottom = 0
            }
        };

        var label = new Label(labelText)
        {
            style =
            {
                width = labelWidth,
                unityTextAlign = TextAnchor.MiddleLeft,
                marginRight = spacing,
                paddingTop = 0,
                paddingBottom = 0
            }
        };

        row.Add(label);
        
        if (fieldElement != null)
        {
            fieldElement.style.flexGrow = 1;
            fieldElement.style.flexShrink = 1;
            fieldElement.style.flexBasis = 0;
            fieldElement.style.marginTop = 0;
            fieldElement.style.marginBottom = 0;
            fieldElement.style.paddingTop = 0;
            fieldElement.style.paddingBottom = 0;
            row.Add(fieldElement);
        }

        return row;
    }
}
