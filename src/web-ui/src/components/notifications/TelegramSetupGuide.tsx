import React from 'react';

/**
 * Step-by-step guide to make a Telegram bot and find the two values that the target form needs
 * (feature 120). Each step that gives a value names the form field that gets it.
 */
export const TelegramSetupGuide: React.FC = () => (
  <section className="telegram-setup-guide" aria-label="Telegram setup guide">
    <h3>How to set up Telegram</h3>
    <ol>
      <li>Open Telegram and search for <strong>@BotFather</strong>. Open the chat with it.</li>
      <li>Send the message <code>/newbot</code>. Answer the two questions to name your bot.</li>
      <li>
        BotFather sends a bot token. Copy it. Paste it in the field <strong>Bot token</strong> of the form.
      </li>
      <li>
        Open a chat with your new bot and press <strong>Start</strong>. To use a group, add the bot to
        the group.
      </li>
      <li>Send one message to the bot, or to the group.</li>
      <li>
        Open this address in a browser: <code>https://api.telegram.org/bot&lt;your token&gt;/getUpdates</code>.
        Find the number after <code>&quot;chat&quot;:&#123;&quot;id&quot;:</code> in the answer. Copy it. Paste it
        in the field <strong>Chat ID</strong> of the form. A group number starts with a minus sign.
      </li>
    </ol>
    <p className="form-hint">Use &quot;Save and test&quot; to send a test message to your chat.</p>
  </section>
);
