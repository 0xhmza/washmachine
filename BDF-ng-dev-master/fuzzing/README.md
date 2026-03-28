# Fuzzing

You need afl-fuzz >= 1.95b

pip3 install python-afl


Example:

Put pe file targets < 1MB in size in pein directory

```
py-afl-fuzz -i pein -o aflout -- ./pefuzzer.py @@
```
