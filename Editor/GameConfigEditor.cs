using UnityEditor;
using UnityEngine;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Editor
{
    [CustomEditor(typeof(GameConfig))]
    internal class GameConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button("Update Configuration Files"))
            {
                ConfigurationGenerator.UpdateFromGameConfig((GameConfig)target);
            }
        }
    }
}
