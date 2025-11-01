using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ArmatureIK))]
public class ArmatureIKEditor : Editor
{
    SerializedProperty selectedOptionProp;

    SerializedProperty usingNavAgentProp;

    SerializedProperty usingRigidbodyProp;

    private void OnEnable()
    {
        selectedOptionProp = serializedObject.FindProperty("SelectedOption");

        usingNavAgentProp = serializedObject.FindProperty("UsingNavMeshAgent");

        usingRigidbodyProp = serializedObject.FindProperty("UsingRigidbody");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(selectedOptionProp);

        // Draw header if at least one ControllerType field will be visible
        bool showNav = selectedOptionProp.enumValueIndex == (int)ArmatureIK.OptionType.NavMeshAgent;
        bool showRB = selectedOptionProp.enumValueIndex == (int)ArmatureIK.OptionType.Rigidbody;
        if (showNav || showRB)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ControllerType", EditorStyles.boldLabel);
        }

        SerializedProperty prop = serializedObject.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (prop.name == "selectedOption" || prop.name == "m_Script")
                continue;

            // Skip fields based on the selected option
            if (showNav && prop.name == "characterRigidBody")
            {
                usingNavAgentProp.boolValue = true;
                usingRigidbodyProp.boolValue = false;
               continue; 
            }

            if (showRB && prop.name == "navAgent")
            {
                usingNavAgentProp.boolValue = false;
                usingRigidbodyProp.boolValue = true;
                continue;
            }
             

            EditorGUILayout.PropertyField(prop, true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
