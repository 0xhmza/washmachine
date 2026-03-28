Being that unittesting would be a bit pedantic for this project, I need to have tests for each patching method, encoders (eventaully), mode, etc..

How this will work: I will need to test each binary output, if it works as expected, sha256 hash that binary, and make sure that this is result we expecting to ensure bug fixes don't break known working outputs.

However, I have to make a 'testing' flag that I pass to each function and ensure randomize output is normalized so the hashes are not themselves randomized. Also, for manual cave selection, I should expose a list/set of pre-selected caves which will allow consistency and to test reverse and forward cave jumping.

To Dos:
1. Expose a testing flag.
2. Nullify random selections if testing flag is set.
2. Expose a cave section set/list that can be passed in.
3. Write a series of test cases across all payloads and chipsets and binary formats. . . . 

---
1. Done

2. Done 

3. Done

4. In progress...
