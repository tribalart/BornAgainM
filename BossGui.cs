using System;
using System.Linq;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppSystem.Linq;
using Il2CppRonin.Model.Enums;


namespace BornAgainM
{
    public class BossMonitor
    {
        private float lastScanTime = 0f;
        private const float SCAN_INTERVAL = 1f;
        private string aliveBossesText = "";
        private string deadBossesText = "";
        private Texture2D bgTexture;

        public BossMonitor()
        {
            // Créer la texture de fond une seule fois
            bgTexture = new Texture2D(400, 300);
            bgTexture.SetPixel(0, 0, new Color(0.05f, 0.1f, 0.2f, 0.7f));
            bgTexture.Apply();
        }

        public void Update()
        {
            if (Time.time - lastScanTime >= SCAN_INTERVAL)
            {
                lastScanTime = Time.time;
                ScanBosses();
            }
        }

        private void ScanBosses()
        {
            try
            {
                var world = World.Instance;
                if (world == null) return;

                var bosses = world.GetBosses();
                if (bosses == null) return;

                aliveBossesText = "";
                deadBossesText = "";

                var count = bosses.Count();
              //  MelonLogger.Msg($"Total boss: {count}");

                for (int i = 0; i < count; i++)
                {
                    var boss = bosses.ElementAt(i);
                    if (boss == null) continue;

                    if (boss.Health == 0)
                    {
                      
                      //  MelonLogger.Msg($"Boss {i}: {boss._entityName} dead");
                        deadBossesText += $"{boss._entityName} ☠ \n";
                        
                        
                    }
                    else
                    {
                      //  MelonLogger.Msg($"Boss {i}: {boss._entityName} hp {boss.Health}");
                        aliveBossesText += $" {boss._entityName} - ❤: {boss.Health}  Def {boss.GetStatFunctional(StatType.Defense)}\n";
                        
                    }
                    
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Erreur scan boss: {ex}");
            }
        }

        public void OnGUI()
        {
            if (string.IsNullOrEmpty(aliveBossesText) && string.IsNullOrEmpty(deadBossesText))
                return;

            float startY = Screen.height / 4f;
            float startX = 20f;
            float width = 400f;
            float height = 300f;

            // Fond
            GUIStyle bgStyle = new GUIStyle();
            bgStyle.normal.background = bgTexture;
            GUI.Box(new Rect(startX - 5, startY - 5, width + 10, height + 10), "", bgStyle);

            float currentY = startY;

            // Boss vivants en VERT
            if (!string.IsNullOrEmpty(aliveBossesText))
            {
                GUIStyle aliveStyle = new GUIStyle();
                aliveStyle.normal.textColor = Color.green;
                aliveStyle.fontSize = 14;
                aliveStyle.fontStyle = FontStyle.Bold;

                GUI.Label(new Rect(startX, currentY, width, height), aliveBossesText, aliveStyle);

                // Calculer la hauteur utilisée
                int lineCount = aliveBossesText.Split('\n').Length;
                currentY += lineCount * 12; // ~22 pixels par ligne
            }

            // Boss morts en ROUGE
            if (!string.IsNullOrEmpty(deadBossesText))
            {
                GUIStyle deadStyle = new GUIStyle();
                deadStyle.normal.textColor = Color.red;
                deadStyle.fontSize = 14;
                deadStyle.fontStyle = FontStyle.Bold;

                GUI.Label(new Rect(startX, currentY, width, height), deadBossesText, deadStyle);
            }
        }
    }
}