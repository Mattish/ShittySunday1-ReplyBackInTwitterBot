Shitty Sunday Evening #1: @ReplyBackIn Twitter Bot
===================================

@ReplyBackIn twitter bot to remind you of up to 140 characters at some given point in the future.

Idea
----------------

The idea is simple. In my sunday evening create a simple bot to respond to a mention given a time format, in which to respond back the message with the given delay.

Did it work?
----------------

Hells yeah...with a few small fixes. Setup your tokens, keys and screenName in the App.config and you are ready to go!
It also works fine with in mono, which is where I run this.

Time Format
----------------

> Match days = Regex.Match(input, @"\d{1,2}d");

> Match hours = Regex.Match(input, @"\d{1,2}h");

> Match minutes = Regex.Match(input, @"\d{1,2}m");

I tried a more complex regex, but it broke.

What did we learn?
----------------

* Cba with complex regex
* Wasted 1 hour getting twitter library to work in Mono
* It's actually pretty useful
* You can make something pretty useful in an evening

What can be improved?
----------------

I might do these at some point

* Program layout
* Whitespace checking
* time format?
* ''Regex''
