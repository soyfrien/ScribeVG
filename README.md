# ScribeVG

<img src="ScribeVG/Assets/ScribeVG.svg" width="96" height="96" alt="ScribeVG icon">

A Windows touchscreen app for practicing kanji handwriting. Write each stroke and ScribeVG checks its order, direction and shape against [KanjiVG](https://kanjivg.tagaini.net) stroke data, then schedules reviews with spaced repetition.

ScribeVG is the open-source successor to UmaiKanji by Tokibits, a [PPDAC](https://ppdac.ltd) company.

**[Install ScribeVG](https://ppdac.ltd/scribevg/)** from the web — it updates itself each time it starts. The Install link works directly in Microsoft Edge. In Chrome or Firefox, open the downloaded file, or use `setup.exe` from the same page. You can also open [ScribeVG.application](https://ppdac.ltd/scribevg/ScribeVG.application) directly. It installs for your Windows account only, with no admin rights needed. The app isn't code-signed yet, so Windows will say the publisher is unknown.

## Features

- Stroke-by-stroke scoring with a stroke-order guide (hold Space to peek)
- Study mode with FSRS spaced repetition and up to 10 new kanji a day, ordered by old JLPT level, school grade and frequency
- A kanji unlocks only after you've learned its parts (水 before 氷)
- Sheet mode, like a practice notebook: one traced rep, two faded reps, then three from memory
- Meanings stay hidden until you finish, so you have to recall them
- Example sentences for about 1,900 kanji, with Emi Asano's readings
- Tap the big kanji for a speech bubble with an example sentence, and Emi's voice when a clip is available
- Sentence audio recorded by Emi Asano is built into the app and works offline
- Auto-advance, visual feedback and a practice-paper look

Your progress is kept in `%AppData%\ScribeVG` and carries over between updates.

## Build

You need the .NET SDK with Windows desktop support:

    dotnet build ScribeVG/ScribeVG.csproj

## Credits

- Sentence audio: recorded by [Emi Asano](https://www.linkedin.com/in/emi-asano-7b752557/). Recordings copyright Tokibits/PPDAC.
- Example sentences: the [Tatoeba Project](https://tatoeba.org), CC BY 2.0 FR
- Stroke data: [KanjiVG](https://kanjivg.tagaini.net) by Ulrich Apel and contributors, CC BY-SA 3.0
- Kanji meanings, readings and grades: [KANJIDIC2](https://www.edrdg.org/wiki/index.php/KANJIDIC_Project) from the Electronic Dictionary Research and Development Group, CC BY-SA 4.0

## License

The code is MIT; see `LICENSE.txt`. The bundled data and the sentence recordings keep the licenses listed above.
