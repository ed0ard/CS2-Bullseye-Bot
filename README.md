# CS2-Bullseye-Bot
CS2-Bullseye-Bot is a CounterStrikeSharp plugin that improves bots' aim in CS2.

Bots choose aim points on the target's animated server hitboxes. If cover hides
the usual head point, the bot can also check just inside the top of the head
capsule while acquiring an enemy. A shot trace must hit the intended player
and hitgroup, and the engine still checks visibility before the bot aims there.

![bullseye_2](https://github.com/user-attachments/assets/b658e64e-028c-42de-817c-1748fe0ca4f4)

# Commands

`bot_aim mixed`  
Bots select aiming spots flexibly based on situations (default)

`bot_aim head`  
Bots prioritize aiming at the head (the AWP keeps its torso preference)

`bot_aim body`  
Bots prioritize aiming at the torso

`bot_aim`  
Check the current aim mode
# Installation

1. Download the latest **BotAimImprover.zip** from [Releases](https://github.com/ed0ard/CS2-Bullseye-Bot/releases)

2. Extract the folder and upload it to `game/csgo/addons/counterstrikesharp/plugins` on your server
## If you find the plugin useful then please take the time to star⭐ the repository
