using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SuperQQ.UI
{
    /// <summary>
    /// Bind category buttons (Cat_XXX) to sub option rows (SubOptionsRow_XXX).
    /// Click a category -> show its row, hide others.
    /// </summary>
    public class CategoryTabSwitcher : MonoBehaviour
    {
        [Serializable]
        public class CategoryEntry
        {
            public Button categoryButton;
            public GameObject subOptionsRow;
        }

        [SerializeField] private List<CategoryEntry> entries = new List<CategoryEntry>();
        [SerializeField] private int defaultIndex = 0;

        [Header("Optional highlight")]
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color selectedColor = new Color(1f, 0.85f, 0.4f, 1f);

        private void Start()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                int index = i;
                var entry = entries[i];
                if (entry.categoryButton != null)
                {
                    entry.categoryButton.onClick.RemoveListener(() => Select(index));
                    entry.categoryButton.onClick.AddListener(() => Select(index));
                }
            }

            if (entries.Count > 0)
            {
                Select(Mathf.Clamp(defaultIndex, 0, entries.Count - 1));
            }
        }

        public void Select(int index)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                bool on = (i == index);
                if (entry.subOptionsRow != null)
                {
                    entry.subOptionsRow.SetActive(on);
                }
                if (entry.categoryButton != null)
                {
                    var img = entry.categoryButton.GetComponent<Image>();
                    if (img != null) img.color = on ? selectedColor : normalColor;
                }
            }
        }
    }
}
