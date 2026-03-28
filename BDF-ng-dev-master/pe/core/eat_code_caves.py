def eat_code_caves(flItms, caveone, cavetwo):
    """
    Return the difference between caves RVA positions
    """

    try:
        if flItms['CavesPicked'][cavetwo][0] == flItms['CavesPicked'][caveone][0]:
            return int(flItms['CavesPicked'][cavetwo][1], 16) - int(flItms['CavesPicked'][caveone][1], 16)

        else:
            caveone_found = False
            cavetwo_found = False
            for section in flItms['Sections']:
                if flItms['CavesPicked'][caveone][0] == section[0] and caveone_found is False:
                    rva_one = int(flItms['CavesPicked'][caveone][1], 16) - int(flItms['CavesPicked'][caveone][4], 16) + flItms['CavesPicked'][caveone][8]
                    caveone_found = True

                if flItms['CavesPicked'][cavetwo][0] == section[0] and cavetwo_found is False:
                    rva_two = int(flItms['CavesPicked'][cavetwo][1], 16) - int(flItms['CavesPicked'][cavetwo][4], 16) + flItms['CavesPicked'][cavetwo][8]
                    cavetwo_found = True

                if caveone_found is True and cavetwo_found is True:
                    if flItms['CavesPicked'][caveone][1] < flItms['CavesPicked'][cavetwo][1]:
                        return -(rva_one - rva_two)
                    else:
                        return rva_two - rva_one

    except Exception as e:
        return 0

