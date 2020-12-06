using System;
using System.Collections.Generic;
using System.IO;
using Aseprite;
using Aseprite.Utils;
using UnityEditor;
using UnityEngine;

namespace AsepriteImporter {
    public class AseTileImporter {
        private AseFileTextureSettings settings;
        private int padding = 1;
        private Vector2Int size;
        private string fileName;
        private string filePath;
        private int updateLimit;
        private Texture2D atlas;
        
        public void Import(string path, AseFile file, AseFileTextureSettings settings) {
            this.settings = settings;
            this.size = new Vector2Int(file.Header.Width, file.Header.Height); 

            Texture2D frame = file.GetFrames()[0];
            BuildAtlas(path, frame);
            
            updateLimit = 300;
            EditorApplication.update += OnUpdate;
        }

        private void OnUpdate() {
            AssetDatabase.Refresh();
            var done = false;
            if (GenerateSprites(filePath, settings, size)) {
                done = true;
            } else {
                updateLimit--;
                if (updateLimit <= 0) {
                    done = true;
                }
            }

            if (done) {
                EditorApplication.update -= OnUpdate;
            }
        }

        private void BuildAtlas(string acePath, Texture2D sprite) {
            fileName= Path.GetFileNameWithoutExtension(acePath);
            var directoryName = Path.GetDirectoryName(acePath) + "/" + fileName;
            if (!AssetDatabase.IsValidFolder(directoryName)) {
                AssetDatabase.CreateFolder(Path.GetDirectoryName(acePath), fileName);
            }

            filePath = directoryName + "/" + fileName + ".png";

            GenerateAtlas(sprite);
            try {
                File.WriteAllBytes(filePath, atlas.EncodeToPNG());
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            } catch (Exception e) {
                Debug.LogError(e.Message);
            }
        }

        public void GenerateAtlas(Texture2D sprite) {
            var spriteSizeW = settings.tileSize.x + padding * 2;
            var spriteSizeH = settings.tileSize.y + padding * 2;
            var cols = sprite.width / settings.tileSize.x;
            var rows = sprite.height / settings.tileSize.y;
            var width = cols * spriteSizeW;
            var height = rows * spriteSizeH;

            atlas = Texture2DUtil.CreateTransparentTexture(width, height);
            for (var row = 0; row < rows; row++) {
                for (var col = 0; col < cols; col++) {
                    RectInt from = new RectInt(col * settings.tileSize.x,
                                               row * settings.tileSize.y,
                                               settings.tileSize.x,
                                               settings.tileSize.y);
                    RectInt to = new RectInt(col * spriteSizeW + padding, 
                                                 row * spriteSizeH + padding,
                                                 settings.tileSize.x, 
                                                 settings.tileSize.y);
                    CopyColors(sprite, atlas, from, to);
                    atlas.Apply();
                }
            }
        }

        private Color[] GetPixels(Texture2D sprite, RectInt from) {
            var res = sprite.GetPixels(from.x, from.y, from.width, from.height);
            if (settings.transparencyMode == TransparencyMode.Mask) {
                for (int index = 0; index < res.Length; index++) {
                    var color = res[index];
                    if (color == settings.transparentColor) {
                        color.r = color.g = color.b = color.a = 0;
                        res[index] = color;
                    }
                }
            }

            return res;
        }

        private Color GetPixel(Texture2D sprite, int x, int y) {
            var color = sprite.GetPixel(x, y);
            if (settings.transparencyMode == TransparencyMode.Mask) {
                if (color == settings.transparentColor) {
                    color.r = color.g = color.b = color.a = 0; 
                }
            }
            
            return color;
        }

        private void CopyColors(Texture2D sprite, Texture2D atlas, RectInt from, RectInt to) {
            atlas.SetPixels(to.x, to.y, to.width, to.height, GetPixels(sprite, from));

            for (int index = 0; index < padding; index++) {
                RectInt lf = new RectInt(from.x, from.y, 1, from.height);
                RectInt lt = new RectInt(to.x - index - 1, to.y, 1, to.height);
                RectInt rf = new RectInt(from.xMax - 1, from.y, 1, from.height);
                RectInt rt = new RectInt(to.xMax + index, to.y, 1, to.height);
                atlas.SetPixels(lt.x, lt.y, lt.width, lt.height, GetPixels(sprite, lf));
                atlas.SetPixels(rt.x, rt.y, rt.width, rt.height, GetPixels(sprite, rf));
            }

            for (int index = 0; index < padding; index++) {
                RectInt tf = new RectInt(from.x, from.y, from.width, 1);
                RectInt tt = new RectInt(to.x, to.y - index - 1, to.width, 1);
                RectInt bf = new RectInt(from.x, from.yMax - 1, from.width, 1);
                RectInt bt = new RectInt(to.x, to.yMax + index, to.width, 1);
                atlas.SetPixels(tt.x, tt.y, tt.width, tt.height, GetPixels(sprite, tf));
                atlas.SetPixels(bt.x, bt.y, bt.width, bt.height, GetPixels(sprite, bf));
            }

            for (int x = 0; x < padding; x++) {
                for (int y = 0; y < padding; y++) {
                    atlas.SetPixel(to.x - x - 1, to.y - y - 1, GetPixel(sprite, from.x, from.y));
                    atlas.SetPixel(to.xMax + x, to.y - y - 1, GetPixel(sprite, from.xMax - 1, from.y));
                    atlas.SetPixel(to.x - x - 1, to.yMax + y, GetPixel(sprite, from.x, from.yMax - 1));
                    atlas.SetPixel(to.xMax + x, to.yMax + y, GetPixel(sprite, from.xMax - 1, from.yMax - 1));
                }
            }
        }
        
        private bool GenerateSprites(string path, AseFileTextureSettings settings, Vector2Int size) {
            this.settings = settings;
            this.size = size; 

            var fileName = Path.GetFileNameWithoutExtension(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) {
                return false;
            }
            
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = settings.pixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            var metaList = CreateMetaData(fileName);
            var properties = AseSpritePostProcess.GetPhysicsShapeProperties(importer, metaList);
            
            importer.spritesheet = metaList.ToArray();
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spriteImportMode = SpriteImportMode.Multiple;

            EditorUtility.SetDirty(importer);
            try {
                importer.SaveAndReimport();
            } catch (Exception e) {
                Debug.LogWarning("There was a problem with generating sprite file: " + e);
            }

            AseSpritePostProcess.RecoverPhysicsShapeProperty(properties);
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
            return true;
        }

        private List<SpriteMetaData> CreateMetaData(string fileName) {
            var tileSize = settings.tileSize;
            var cols = size.x / tileSize.x;
            var rows = size.y / tileSize.y;
            var res = new List<SpriteMetaData>();
            var index = 0;
            var height = rows * (tileSize.y + padding * 2);
            
            for (var row = 0; row < rows; row++) {
                for (var col = 0; col < cols; col++) {
                    Rect rect = new Rect(col * (tileSize.x + padding * 2) + padding,
                                         height - (row + 1) * (tileSize.y + padding * 2) + padding, 
                                         tileSize.x,
                                         tileSize.y);
                    var meta = new SpriteMetaData();
                    if (settings.tileEmpty == EmptyTileBehaviour.Remove && IsTileEmpty(rect, atlas)) {
                        index++;
                        continue;
                    }
                    
                    meta.name = fileName + "_" + index;
                    if (settings.tileNameType == TileNameType.RowCol) {
                        meta.name = GetRowColTileSpriteName(fileName, col, row, cols, rows);
                    }
                    
                    meta.rect = rect;
                    meta.alignment = settings.spriteAlignment;
                    meta.pivot = settings.spritePivot;
                    res.Add(meta);
                    
                    index++;
                }
            }

            return res;
        }
        
        private string GetRowColTileSpriteName(string fileName, int x, int y, int cols, int rows) {
            int yHat = y;
            string row = yHat.ToString();
            string col = x.ToString();
            if (rows > 100) {
                row = yHat.ToString("D3");
            } else if (rows > 10) {
                row = yHat.ToString("D2");
            }

            if (cols > 100) {
                col = x.ToString("D3");
            } else if (cols > 10) {
                col = x.ToString("D2");
            }

            return string.Format("{0}_{1}_{2}", fileName, row, col);
        }
        
        private SerializedProperty GetPhysicsShapeProperty(TextureImporter importer, string spriteName) {
            SerializedObject serializedImporter = new SerializedObject(importer);
 
            if (importer.spriteImportMode == SpriteImportMode.Multiple) {
                var spriteSheetSP = serializedImporter.FindProperty("m_SpriteSheet.m_Sprites");
 
                for (int i = 0; i < spriteSheetSP.arraySize; i++) {
                    if (importer.spritesheet[i].name == spriteName) {
                        var element = spriteSheetSP.GetArrayElementAtIndex(i);
                        return element.FindPropertyRelative("m_PhysicsShape");
                    }
                }
 
            }
 
            return serializedImporter.FindProperty("m_SpriteSheet.m_PhysicsShape");
        }
        
        private bool IsTileEmpty(Rect tileRect, Texture2D atlas) {
            Color[] tilePixels = atlas.GetPixels((int)tileRect.xMin, (int)tileRect.yMin, (int)tileRect.width, (int)tileRect.height);
            for (int i = 0; i < tilePixels.Length; i++) {
                if (tilePixels[i].a != 0) {
                    return false;
                } 
            }
            return true;
        }
    }
}