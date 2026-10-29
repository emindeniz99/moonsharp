-- bit32: argument counts, shift ranges, field widths, signedness and rounding.
--@@ band none
return bit32.band()
--@@ bor none
return bit32.bor()
--@@ bxor none
return bit32.bxor()
--@@ btest none
return bit32.btest()
--@@ band negative
return bit32.band(-1), bit32.band(-2^33 - 1), bit32.band(2^40 - 4)
--@@ band fraction
return bit32.band(1.5), bit32.band(2.5), bit32.band(-1.5), bit32.band(0.4999)
--@@ bnot
return bit32.bnot(0), bit32.bnot(-1), bit32.bnot(2^32)
--@@ lshift ranges
return bit32.lshift(1, 31), bit32.lshift(1, 32), bit32.lshift(1, 33), bit32.lshift(1, -1), bit32.lshift(2, -1), bit32.lshift(0xffffffff, -32), bit32.lshift(1, 64)
--@@ rshift ranges
return bit32.rshift(0x80000000, 31), bit32.rshift(0x80000000, 32), bit32.rshift(1, -31), bit32.rshift(1, -32), bit32.rshift(0xffffffff, 40)
--@@ arshift
return bit32.arshift(-1, 1), bit32.arshift(-1, 32), bit32.arshift(0x80000000, 4), bit32.arshift(0x7fffffff, 40), bit32.arshift(-1, -1), bit32.arshift(1, -31)
--@@ rotate
return bit32.lrotate(0x12345678, 4), bit32.rrotate(0x12345678, 4), bit32.lrotate(1, 32), bit32.lrotate(1, -1), bit32.rrotate(1, 33)
--@@ extract top bits
return bit32.extract(0xa0001111, 28, 4), bit32.extract(0xa0001111, 31), bit32.extract(0xf2345679, 0, 32)
--@@ extract errors
return pcall(bit32.extract, 0, 32), pcall(bit32.extract, 0, 0, 33), pcall(bit32.extract, 0, -1), pcall(bit32.extract, 0, 0, 0)
--@@ replace
return bit32.replace(0, 1, 2), bit32.replace(0x12345678, 5, 28, 4), bit32.replace(-1, 0, 31), bit32.replace(0, 0xffffffff, 0, 32)
--@@ replace width default
return bit32.replace(0, 3, 4)
--@@ non number argument
return pcall(bit32.band, "3", 1), pcall(bit32.band, {}, 1)
--@@ result is unsigned
return bit32.bor(0x80000000, 0) > 0, bit32.bnot(0) > 0
