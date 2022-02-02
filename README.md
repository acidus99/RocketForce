# RocketForce
RocketForce is a [Gemini](https://gemini.circumlunar.space/docs/) Server in .NET. It can serve static files and supports application development via callback functions on specific routes. It is written in .NET 5 and supports Windows, Linux, and macOS.

![Gemini III, first crewed mission, launching into space](gemini-iii-launch.jpg)

RocketForce provides many features beyond existing .NET gemini servers:

- Serving static files (without needing a specific route per file)
- Support text and binary files
- Default files (e.g. `/directory/` will serve `/directory/index.gmi`)
- Streaming output to the client (vs buffering until entire response is generated)

RocketForce is [inspired by JetForce](https://github.com/michael-lazar/jetforce). RocketForce started as a heavily modified version of [Cuipod](https://github.com/aegis-dev/cuipod)
