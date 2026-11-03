namespace GDSViewer.Ai
{
    ///
    ///A key saved with a model, kept to the transport it was saved with: a saved key is never sent in plain text
    ///without somebody typing it again.
    ///
    ///Plain http is anybody's choice and is never refused - a server of one's own on the same machine is the usual
    ///reason for it. But a form holding a saved model, key and all, sends the key with whatever the form says next,
    ///so changing the address to an http one would send and save the saved key unencrypted with nobody having typed
    ///it. So the moment the form's address stops being encrypted, the saved key leaves the form; it comes back if
    ///the address is made encrypted again before anybody types another. A key typed meanwhile is the person's own
    ///entry and is left alone, and a model saved without encryption keeps its key, since somebody chose that.
    ///
    ///Ported from GraphDBViewer, where the rule was made on 2026-10-03 for every form that holds a secret.
    ///
    public sealed class SavedSecretRule
    {
        private string? saved;
        private bool savedEncrypted;

        ///<summary>True while the saved key is out of the form because its address is no longer encrypted.</summary>
        public bool HeldBack { get; private set; }

        ///<summary>What the form was loaded from or just saved as: its key, and whether its address was encrypted.</summary>
        public void Remember(string? secret, bool encrypted)
        {
            saved = secret;
            savedEncrypted = encrypted;
            HeldBack = false;
        }

        ///<summary>The key the form should hold, given the one it holds and whether its address is encrypted now.</summary>
        public string? Keep(string? current, bool encrypted)
        {
            if (string.IsNullOrEmpty(saved) || !savedEncrypted)
                return current;

            if (!encrypted && !HeldBack && current == saved)
            {
                HeldBack = true;

                return null;
            }
            else if (encrypted && HeldBack)
            {
                HeldBack = false;

                if (string.IsNullOrEmpty(current))
                    return saved;
            }

            return current;
        }

        ///<summary>Whether the form should say why its key field is empty: held back, and nothing typed since.</summary>
        public bool Says(string? current)
        {
            return HeldBack && string.IsNullOrEmpty(current);
        }
    }
}
