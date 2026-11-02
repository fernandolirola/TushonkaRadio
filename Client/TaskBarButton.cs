using System;
using System.Linq;
using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace HideoutRadio;

internal sealed class TaskBarButton
{
    private readonly GameObject _clone;
    private readonly AnimatedToggle _toggle;
    private readonly TextMeshProUGUI? _label;
    private readonly HoverTooltipArea? _tooltip;
    private static Sprite? _icon;
    private readonly float _created = Time.unscaledTime;
    private TaskBarButton(GameObject clone, AnimatedToggle toggle, TextMeshProUGUI? label)
    { _clone = clone; _toggle = toggle; _label = label; _tooltip = clone.GetComponentInChildren<HoverTooltipArea>(true); }
    public bool Alive => _clone != null;
    public void Destroy() { if (_clone != null) UnityEngine.Object.Destroy(_clone); }
    public void Sync(bool visible, bool selected, bool pt)
    {
        if (_clone == null) return;
        if (_clone.activeSelf != visible) _clone.SetActive(visible);
        if (!visible) return;
        if (_toggle.isOn != selected) _toggle.ToggleSilent(selected);
        if (_label != null)
        {
            if (Time.unscaledTime - _created > 1.5f && (!_label.gameObject.activeInHierarchy || _label.canvasRenderer.GetAlpha() < .5f))
            { var animator = _toggle.GetComponent<Animator>(); if (animator != null) animator.enabled = false; }
            for (var t = _label.transform; t != null && t != _clone.transform; t = t.parent) t.gameObject.SetActive(true);
            _label.enabled = true; _label.gameObject.SetActive(true);
            _label.text = pt ? "R\u00c1DIO" : "RADIO";
            var c = _label.color; c.a = 1; _label.color = c; _label.canvasRenderer.SetAlpha(1);
        }
        _tooltip?.SetMessageText(pt ? "Abrir o player de m\u00fasica" : "Open the music player", true);
    }
    public static TaskBarButton? Create(MenuTaskBar taskBar, Action open)
    {
        if (!taskBar._toggleButtons.TryGetValue(EMenuType.Hideout, out var hideout) || hideout == null) return null;
        var template = hideout.transform;
        while (template.parent != null && template.parent.name != "Tabs") template = template.parent;
        if (template.parent == null) template = hideout.transform;
        var parent = template.parent;
        if (parent == null || parent.Find("HideoutRadioTaskBarButton") != null) return null;
        var clone = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        clone.name = "HideoutRadioTaskBarButton";
        var anchor = parent.Find("CraftQueueTaskBarButton") ?? template;
        clone.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);
        var toggle = clone.GetComponentInChildren<AnimatedToggle>(true);
        if (toggle == null) { UnityEngine.Object.Destroy(clone); return null; }
        toggle.group = null;
        toggle.onValueChanged = new Toggle.ToggleEvent();
        toggle.gameObject.AddComponent<RadioClickHandler>().Open = open;
        toggle.ToggleSilent(false);
        var notification = clone.transform.Find("NewInformation");
        if (notification != null) notification.gameObject.SetActive(false);
        foreach (var localized in clone.GetComponentsInChildren<LocalizedText>(true))
        { localized.enabled = false; UnityEngine.Object.Destroy(localized); }
        foreach (var group in clone.GetComponentsInChildren<CanvasGroup>(true))
        { group.alpha = 1; group.interactable = true; group.blocksRaycasts = true; }
        var label = clone.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            for (var t = label.transform; t != null && t != clone.transform; t = t.parent) t.gameObject.SetActive(true);
            label.text = "RADIO";
        }
        var image = clone.GetComponentsInChildren<Image>(true).FirstOrDefault(x => x.gameObject.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0);
        if (image != null)
        {
            if (_icon == null)
            {
                var texture = new Texture2D(48, 48, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color[48 * 48];
                for (int y = 0; y < 48; y++) for (int x = 0; x < 48; x++)
                {
                    bool stem = x >= 28 && x <= 32 && y >= 12 && y <= 39;
                    bool flag = x >= 29 && x <= 41 && y >= 31 && y <= 39;
                    bool note = (x - 23) * (x - 23) / 1.5f + (y - 12) * (y - 12) <= 48;
                    pixels[y * 48 + x] = stem || flag || note ? Color.white : Color.clear;
                }
                texture.SetPixels(pixels); texture.Apply();
                float units = image.sprite != null ? 48 * image.sprite.pixelsPerUnit / image.sprite.rect.width : 100;
                _icon = Sprite.Create(texture, new Rect(0, 0, 48, 48), new Vector2(.5f, .5f), units);
            }
            image.sprite = _icon; image.overrideSprite = null; image.preserveAspect = true;
        }
        return new TaskBarButton(clone, toggle, label);
    }
}


internal sealed class RadioClickHandler : MonoBehaviour, IPointerClickHandler, ISubmitHandler
{
    internal Action? Open;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) Open?.Invoke();
    }

    public void OnSubmit(BaseEventData eventData) => Open?.Invoke();
}
