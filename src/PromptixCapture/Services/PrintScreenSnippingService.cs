using Microsoft.Win32;

namespace PromptixCapture.Services;

/// <summary>Keeps Windows Snipping Tool from also reacting to Print Screen while LoviKadr is running.</summary>
public sealed class PrintScreenSnippingService : IDisposable
{
    private const string KeyPath = @"Control Panel\Keyboard";
    private const string ValueName = "PrintScreenKeyForSnippingEnabled";
    private object? _previous;
    private bool _hadPrevious;
    private bool _changed;

    public void DisableForThisSession()
    {
        try
        {
            using var key=Registry.CurrentUser.CreateSubKey(KeyPath, writable:true);
            _previous=key.GetValue(ValueName);_hadPrevious=_previous is not null;
            if(_previous is int value && value==0)return;
            key.SetValue(ValueName,0,RegistryValueKind.DWord);_changed=true;
        }
        catch(Exception ex){AppLog.Error("Disable Windows Print Screen snipping",ex);}
    }

    public void Dispose()
    {
        if(!_changed)return;
        try
        {
            using var key=Registry.CurrentUser.CreateSubKey(KeyPath, writable:true);
            if(_hadPrevious)key.SetValue(ValueName,_previous!);
            else key.DeleteValue(ValueName, throwOnMissingValue:false);
        }
        catch(Exception ex){AppLog.Error("Restore Windows Print Screen snipping",ex);}
    }
}
