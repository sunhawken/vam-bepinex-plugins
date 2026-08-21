using System;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ZeroT;

[BepInPlugin("zerot.postmagic.toggle", "PostMagic Toggle", "2.0.0")]
public class PostMagicToggle : BaseUnityPlugin
{
	private ConfigEntry<KeyboardShortcut> _toggleKey;

	private void Awake()
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		_toggleKey = ((BaseUnityPlugin)this).Config.Bind<KeyboardShortcut>("Hotkeys", "TogglePostMagic", new KeyboardShortcut((KeyCode)112, (KeyCode[])(object)new KeyCode[1] { (KeyCode)308 }), "Alt+P to toggle MacGruber PostMagic on/off");
		Logger.LogInfo((object)$"[PMT] v2.0 ready — hotkey={_toggleKey.Value}");
	}

	private void Update()
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		if (_toggleKey != null)
		{
			KeyboardShortcut value = _toggleKey.Value;
			if (value.IsDown())
			{
				Toggle();
			}
		}
	}

	private void Toggle()
	{
		int num = 0;
		try
		{
			foreach (string atomUID in SuperController.singleton.GetAtomUIDs())
			{
				Atom atomByUid = SuperController.singleton.GetAtomByUid(atomUID);
				if ((Object)(object)atomByUid == (Object)null)
				{
					continue;
				}
				foreach (string storableID in atomByUid.GetStorableIDs())
				{
					JSONStorable storableByID = atomByUid.GetStorableByID(storableID);
					if (!((Object)(object)storableByID == (Object)null))
					{
						JSONStorableBool boolJSONParam = storableByID.GetBoolJSONParam("PostMagicEnabled");
						if (boolJSONParam != null)
						{
							boolJSONParam.val = !boolJSONParam.val;
							Logger.LogInfo((object)$"[PMT] {atomUID}/{storableID} PostMagicEnabled -> {boolJSONParam.val}");
							num++;
						}
					}
				}
			}
		}
		catch (Exception arg)
		{
			Logger.LogError((object)$"[PMT] {arg}");
		}
		if (num > 0)
		{
			Logger.LogInfo((object)$"[PMT] toggled {num} instance(s)");
		}
		else
		{
			Logger.LogWarning((object)"[PMT] no PostMagic found in scene");
		}
	}
}
