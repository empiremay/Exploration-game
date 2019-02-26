﻿using UnityEngine.Audio;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioManager : MonoBehaviour {

    public Sound[] sounds;

    public AstronautManager astronautManager;
    public static AudioManager instance;
    private GameObject[] astronauts;

    private AudioSource[] soundtracks;
    private int playingSoundtrack;  //id of soundtrack that is playing currently
    private int latestSoundtrack;   //id of latest soundtrack for no repeat
    private float timeBetweenSoundtracks = 2;
    private float timeBetweenSoundtracksCounter = 0;

    private int stepCounter = 0;

    private void AssignSoundToObject(Sound sound)
    {
        if (sound.type == "Step")       //Astronaut's Step
        {
            foreach (GameObject astronaut in astronauts)
            {
                astronaut.AddComponent<AudioSource>();

                astronaut.GetComponents<AudioSource>()[stepCounter].clip = sound.clip;
                astronaut.GetComponents<AudioSource>()[stepCounter].volume = sound.volume;
                astronaut.GetComponents<AudioSource>()[stepCounter].pitch = sound.pitch;
                astronaut.GetComponents<AudioSource>()[stepCounter].loop = sound.loop;
                astronaut.GetComponents<AudioSource>()[stepCounter].spatialBlend = 1;
            }
            stepCounter++;
        }
        else
        {
            if(sound.type == "Soundtrack")
            {
                sound.source = gameObject.AddComponent<AudioSource>();  //Añadir al mundo en sí
                sound.source.clip = sound.clip;
                sound.source.volume = sound.volume;
                sound.source.pitch = sound.pitch;
                sound.source.loop = sound.loop;
            }
        }
    }

	void Awake () {

        if (instance == null)
            instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);  //Para que no se corte entre carga de escenas

        astronauts = astronautManager.astronauts;

		foreach(Sound sound in sounds)
        {
            AssignSoundToObject(sound);
        }
	}

    private void Start()
    {
        this.soundtracks = gameObject.GetComponents<AudioSource>();
        playingSoundtrack = Mathf.RoundToInt(UnityEngine.Random.Range(0f, (float)(soundtracks.Length - 1)));
        latestSoundtrack = playingSoundtrack;
        soundtracks[playingSoundtrack].Play();
    }

    private void Update()
    {
        if(!soundtracks[playingSoundtrack].isPlaying)
        {
            timeBetweenSoundtracksCounter += Time.deltaTime;
            if (timeBetweenSoundtracksCounter > timeBetweenSoundtracks)
            {
                timeBetweenSoundtracksCounter = 0;
                //DO THINGS EVERY timeBetweenSoundtracks SECONDS
                playingSoundtrack = Mathf.RoundToInt(UnityEngine.Random.Range(0f, (float)(soundtracks.Length - 1)));
                while(playingSoundtrack == latestSoundtrack)
                {
                    playingSoundtrack = Mathf.RoundToInt(UnityEngine.Random.Range(0f, (float)(soundtracks.Length - 1)));
                }
                soundtracks[playingSoundtrack].Play();
                latestSoundtrack = playingSoundtrack;
            }
        }
    }

    public void PlayStep(int astronautId, int stepId)
    {
        AudioSource step = astronauts[astronautId].GetComponents<AudioSource>()[stepId];
        step.Play();
    }

    public void StopStep(int astronautId, int stepId)
    {
        AudioSource step = astronauts[astronautId].GetComponents<AudioSource>()[stepId];
        step.Stop();
    }

    public bool isPlayingStep(int astronautId, int stepId)
    {
        AudioSource step = astronauts[astronautId].GetComponents<AudioSource>()[stepId];
        return step.isPlaying;
    }
}
