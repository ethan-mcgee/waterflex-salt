# WaterFlex Factory Helper

## What this is

The WaterFlex Factory Helper is a small program that runs on your workstation and talks to the
WaterFlex sensor over USB. It flashes the approved firmware onto the sensor and writes its identity,
then the WaterFlex web console takes over to finish setting it up. You do not need any technical
background to run it — just a Windows computer with a free USB port.

## Download

Go to the **Releases** page for this project and find the latest release. You'll see two files:

- **`WaterFlexFactoryHelper-staging.exe`** — use this only if you were told you're working in the
  staging/test environment.
- **`WaterFlexFactoryHelper-production.exe`** — use this for normal factory-floor production work.

If you're not sure which one to use, ask your supervisor. Click the file name to download it.

## Run it

1. Double-click the downloaded `.exe` file.
2. Windows will likely show a blue box titled **"Windows protected your PC"** — this is expected for
   a new internal tool and does not mean anything is wrong. Click **More info**, then click
   **Run anyway**.
3. A black window (the console) will open and stay open. Leave it open — it's how the helper keeps
   running.
4. The first time it starts, it downloads the approved firmware from WaterFlex. This can take a
   little while depending on your connection; you don't need to do anything while it works.
5. Once you see a line that says the helper is ready, plug in the WaterFlex sensor over USB and
   switch to the WaterFlex web console in your browser to start provisioning that unit.

## The black console window

That window is the helper doing its job in the background — flashing firmware, writing the sensor's
identity, and checking its work. You'll see status messages scroll by; you don't need to read them
unless something goes wrong (see Troubleshooting below). **Do not close this window** while a sensor
is being provisioned, or the job will be interrupted.

## Hand back to the web console

Once the helper says it's ready, all the real work happens in the WaterFlex web console in your
browser. It will tell you when to plug in a sensor, when flashing is happening, and when a unit has
passed or failed its checks. The console is where you'll spend most of your time — the black window
just needs to stay open alongside it.

## Troubleshooting

| What you see | What it means | What to do |
| --- | --- | --- |
| "Windows protected your PC" | Normal for a new internal tool | Click **More info** → **Run anyway** |
| "Could not reach WaterFlex to fetch the approved firmware bundle..." | The helper can't reach the WaterFlex server on startup, and doesn't have a firmware copy saved from a previous run | Check your network/Wi-Fi/VPN connection, then try running the helper again |
| "Could not reach WaterFlex to authorize flashing..." | The helper can't reach the WaterFlex server while trying to flash a sensor | Check your network/Wi-Fi/VPN connection and try that unit again from the web console |
| "WaterFlex denied flash authorization for this sensor" | The web console hasn't cleared this specific sensor to be flashed yet, or already did | Go back to the web console and make sure you started the job for this sensor there first |
| "The local firmware bundle is not the version approved by WaterFlex" | The firmware on this workstation doesn't match what WaterFlex currently expects | Close and reopen the helper so it can fetch the current approved version; if it still happens, tell your supervisor |
| "Another sensor is already being provisioned on this workstation" | Only one sensor can be worked on per workstation at a time | Wait for the current sensor's job to finish (or fail) before starting another |
| The black window closes on its own | The helper crashed or was closed accidentally | Reopen the `.exe` and check the current sensor's status in the web console before continuing |

If something doesn't match anything above, or keeps happening after you've tried the fix, stop and
contact:

**Who to contact if stuck:** _(fill in with the actual on-call/support contact before first real
use)_
