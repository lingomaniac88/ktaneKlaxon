import os

oggs = filter(lambda f: f.endswith('ogg'), os.listdir())

for ogg in oggs:
    os.system('ffmpeg -i {0} -filter:a \"volume=-5dB\" tmp_{0}'.format(ogg))
    os.system('del {0}'.format(ogg))
    os.system('rename tmp_{0} {0}'.format(ogg))
