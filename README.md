# AeroVisApp

AeroVisApp is a Windows desktop application (WPF, .NET) built to drive and visualize experiments on the **AeroVis Wind Tunnel**.

## Purpose

The AeroVis Wind Tunnel is used to study how air interacts with a 1:24 scale model mounted in the test section — measuring forces and pressures.

Typical use cases include:

- Running guided flight-style scenarios.
- Sweeping wind speed and throttle to characterize a model's aerodynamic behavior.
- Recording drag coefficient, axial forces, wind speed, temperature, humidity, and pressures for later analysis.
- Comparing runs from the simulation history to iterate on a model or a test setup.

## Features

- **Live telemetry dashboard** — temperature, X- and Z-axis forces, wind speed, humidity, drag coefficient, and pressure readings updated in real time.
- **Charts** — live plots of drag coefficient and wind speed over the duration of the run.
- **Tunnel control** — start/stop the run, control throttle and fogger, and pick from preset scenarios or a custom profile.
- **Session timer** — tracks the length of each experiment.
- **Settings**
  - *General*: dark mode.
  - *Device*: throttle sensitivity, fogger power.
  - *Data*: simulation history.
- **Modern UI** — WPF + WPF-UI, with light and dark themes and a splash screen on startup.

## Requirements

- Windows 10/11
- .NET SDK matching the target framework of `AeroVisApp.csproj`
- A connected AeroVis Wind Tunnel for live readings
