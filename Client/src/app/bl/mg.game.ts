import {GameData} from '../entities/game.data';
import {MgThree} from './mg.three';
import {UserData} from '../entities/user.data';
import {ItemData} from '../entities/item.data';
import * as THREE from 'three';
import {TextGeometry} from 'three/examples/jsm/geometries/TextGeometry';
import * as ThreeMeshUI from 'three-mesh-ui';
import {PlayerData} from '../entities/player.data';
import {find, forEach, keys} from 'lodash';
import {V3} from '../entities/V3';
import * as TWEEN from '@tweenjs/tween.js';
import {MathUtils} from 'three';
import {GeneralService} from './general.service';
import {SignalrService} from '../services/SignalrService';

export class MgGame{

  gameData!: GameData;
  mgThree!:MgThree;

  playerData!: PlayerData;
  allItems: { [key: string]: ItemData } = {};
  getPlayerByUserId(userId: string): PlayerData | null | undefined {
    return find(this.gameData.players, p => p.user?.id == userId);
  }

  loadGame(mgThree:MgThree, user:UserData) {
    this.mgThree=mgThree;

    this.playerData = this.getPlayerByUserId(user.id)!;

    console.log("loadGame");
    // console.log(gameData, dayjs().startOf('month').add(1, 'day').set('year', 2018).format('YYYY-MM-DD HH:mm:ss'));
    this.createItem(this.gameData.table, null);

    if (this.playerData) {
      this.mgThree.camera.position.set(this.playerData.camera.position.x, this.playerData.camera.position.y, this.playerData.camera.position.z);
    } else {
      this.mgThree.camera.position.set(this.gameData.observer.position.x, this.gameData.observer.position.y, this.gameData.observer.position.z);
    }

  }


  createItem(itemData: ItemData, parentMesh: THREE.Object3D | null) {
    //console.log("createItem",itemData,parentMesh);

    if (itemData.asset) {
      const frontURL = '\\assets\\games\\' + this.gameData.assets[itemData.asset].frontURL;
      const backURL = '\\assets\\games\\' + (this.gameData.assets[itemData.asset].backURL || this.gameData.assets[itemData.asset].frontURL);
      const assetType = this.gameData.assets[itemData.asset].type;

      if (assetType == "OBJECT") {
        if (frontURL.toLowerCase().endsWith("glb") || frontURL.toLowerCase().endsWith("gltf")) {
          this.mgThree.gltfLoader.load(frontURL, (gltf) => {
            const mesh: THREE.Group = gltf.scene;
            this.processItem(itemData, mesh, parentMesh);
          });
        }

        if (frontURL.toLowerCase().endsWith("stl")) {
          this.mgThree.stlLoader.load(frontURL, (geometry) => {
            const mesh = new THREE.Mesh(geometry);
            this.processItem(itemData, mesh, parentMesh);
          });
        }

        if (frontURL.toLowerCase().endsWith("obj")) {
          this.mgThree.objLoader.load(frontURL, (group) => {
            //     // (object.children[0] as THREE.Mesh).material = material
            //     // object.traverse(function (child) {
            //     //     if ((child as THREE.Mesh).isMesh) {
            //     //         (child as THREE.Mesh).material = material
            //     //     }
            //     // })
            this.processItem(itemData, group, parentMesh);
          });
        }
      }

      if (assetType == "TOKEN") {

        this.mgThree.textureLoader.load(frontURL, frontTexture => {
          // console.log( frontTexture.image.width, frontTexture.image.height );
          let aspect = frontTexture.image.width / frontTexture.image.height;
          let x = 1;
          let z = 1 / aspect;
          if (aspect < 1) {
            z = 1;
            x = aspect;
          }

          var cubeMaterial = [

            new THREE.MeshBasicMaterial({
              //left
              color: 0xffffff, opacity: 0.5, transparent: true
            }),
            new THREE.MeshBasicMaterial({
              //right
              color: 0xffffff, opacity: 0.5, transparent: true
            }),
            new THREE.MeshBasicMaterial({
              // top
              map: frontTexture, transparent: true
            }),
            new THREE.MeshBasicMaterial({
              // bottom
              map: this.mgThree.textureLoader.load(backURL), transparent: true
            }),
            new THREE.MeshBasicMaterial({
              // front
              color: 0xffffff, opacity: 0.5, transparent: true
            }),
            new THREE.MeshBasicMaterial({
              //back
              color: 0xffffff, opacity: 0.5, transparent: true
            })
          ];

          let mesh = new THREE.Mesh(new THREE.BoxGeometry(x, x / 100, z), cubeMaterial);
          this.processItem(itemData, mesh, parentMesh);

        });

      }

      if (assetType == "TEXT3D") {
        this.mgThree.fontLoader.load('https://threejs.org/examples/fonts/helvetiker_regular.typeface.json', (font) => {

          const geometry = new TextGeometry(itemData.text!, {
            font: font,
            size: 0.5,
            height: 0.2,
            curveSegments: 12,
            bevelEnabled: true,
            bevelThickness: 0.03,
            bevelSize: 0.02,
            bevelOffset: 0,
            bevelSegments: 5,
          });
          var textMaterial = new THREE.MeshPhongMaterial(
            {color: 0xff0000, specular: 0xffffff}
          );
          let mesh = new THREE.Mesh(geometry, textMaterial);
          this.processItem(itemData, mesh, parentMesh);
        });

      }
      if (assetType == "TEXTBLOCK") {

        // DOCS ! - RTFM !
        // https://github.com/felixmariotto/three-mesh-ui/wiki/API-documentation
        //
        const container: any = new ThreeMeshUI.Block({

          bestFit: 'auto',
          width: 100,
          height: 100,
          justifyContent: 'center',
          textAlign: 'center',
          fontFamily: 'assets/fonts/Roboto-msdf.json',
          fontTexture: 'assets/fonts/Roboto-msdf.png',
          fontColor: new THREE.Color(0x000000),
          // borderRadius: 0.05,
          backgroundOpacity: 0
        });

        // container.position.set( 0, 0, 0 );
        // container.rotation.x = -0.55;
        // this.scene.add( container );

        const t1: any = new ThreeMeshUI.Text({content: itemData.text});
        container.add(t1);

        this.processItem(itemData, container, parentMesh);

      }
      if (assetType == "SOUND") {

        // console.log(itemData,frontURL,backURL,assetType);

        // create the PositionalAudio object (passing in the listener)
        const sound = new THREE.PositionalAudio(this.mgThree.audioListener);

        // load a sound and set it as the Audio object's buffer
        const audioLoader = new THREE.AudioLoader();
        audioLoader.load(frontURL, function (buffer) {
          sound.setBuffer(buffer);
          sound.setLoop(itemData.playType == "LOOP");
          sound.setVolume(1);
          sound.play();
          // sound.stop()
        });

        let soundGroup = new THREE.Group()
        soundGroup.add(sound);
        this.processItem(itemData, soundGroup, parentMesh);


      }


    } else {
      const mesh: THREE.Group = new THREE.Group()
      this.processItem(itemData, mesh, parentMesh);
    }


  }

  updateItem(new_item: ItemData, parentMesh: any) {
    //console.log("updateItem",new_item, parentMesh);

    let old_item = this.allItems[new_item.id];
    if (!old_item) {
      this.createItem(new_item, parentMesh);
      return;
    }

    old_item.markForDelete = false;

    //update props
    old_item.clickActions = new_item.clickActions;
    old_item.visible = new_item.visible;
    old_item.hoverActions = new_item.hoverActions;

    // update position/scale/rotation/actions....
    this.updateItemPosition(old_item, V3.FromJson(new_item.position));
    this.updateItemScale(old_item, V3.FromJson(new_item.scale))
    this.updateItemRotation(old_item, V3.FromJson(new_item.rotation))

    this.updateItemText(old_item, new_item.text)

    //update all child items
    forEach(new_item.items, new_item => {
      this.updateItem(new_item, old_item.mesh);
    });


    this.handleItemClickActions(old_item);

    this.handleItemVisibility(old_item);
  }

  updateGame(new_game: GameData) {
    //console.log("updateGame",new_game);

    //mark all items to delete - and each item that updated - will be mrked "not"
    forEach(this.allItems, (item, key) => {
      item.markForDelete = true;
    });

    this.updateItem(new_game.table, null);

    forEach(this.allItems, (item, key) => {
      if (item.markForDelete) {
        this.removeAction(item);

        if (item.playType) {
          (item.mesh!.children[0] as THREE.PositionalAudio).stop()
        }
        item.mesh?.parent?.remove(item.mesh);
        delete this.allItems[item.id];
      }
    });

    //players - move / add / remove

  }



  updateItemPosition(item: ItemData, position: V3) {
    //console.log("updateItemPosition", item, position);

    item.position = position;
    // item.mesh!.position.set(position.x, position.y, position.z);
    // this.createMoveAnimation(item.mesh,item.mesh!.position,position)
    if (!GeneralService.deepEqual(position, item.mesh!.position)) {
      new TWEEN.Tween(item.mesh!.position).to(position, 300).start();
    }

  }

  updateItemScale(item: ItemData, scale: V3) {
    //console.log("updateItemScale", item, scale);

    item.scale = scale;
    // item.mesh!.position.set(position.x, position.y, position.z);
    // this.createMoveAnimation(item.mesh,item.mesh!.position,position)
    if (!GeneralService.deepEqual(scale, item.mesh!.scale)) {
      new TWEEN.Tween(item.mesh!.scale).to(scale, 300).start();
    }

  }

  updateItemRotation(item: ItemData, rot: V3) {
    //console.log("updateItemRotation", item, rot);

    item.rotation = rot;

    rot = {
      x: MathUtils.degToRad(rot.x),
      y: MathUtils.degToRad(rot.y),
      z: MathUtils.degToRad(rot.z)
    }

    // item.mesh!.position.set(position.x, position.y, position.z);
    // this.createMoveAnimation(item.mesh,item.mesh!.position,position)
    // let meshRot =new THREE.Vector3();
    // meshRot = meshRot.applyEuler(item.mesh!.rotation);
    if (!GeneralService.deepEqual(rot, {
      x: item.mesh!.rotation.x,
      y: item.mesh!.rotation.y,
      z: item.mesh!.rotation.z
    })) {
      // const u = new THREE.Euler( rot.x, rot.y, rot.z, 'XYZ' )
      new TWEEN.Tween(item.mesh!.rotation).to(rot, 300).start();
    }

  }

  updateItemText(item: ItemData, text?: string) {
    if (item.asset == "TEXTBLOCK") {
      item.text = text;
      (item.mesh! as any).childrenTexts[0].set({content: text});
    }
  }


  processItem(itemData: ItemData, mesh: THREE.Object3D, parentMesh: THREE.Object3D | null) {
    //console.log("processItem",itemData,mesh,parentMesh);

    mesh.name = itemData.asset;

    // position
    mesh.position.set(itemData.position.x, itemData.position.y, itemData.position.z);

    // rotation
    mesh.rotation.set(
      MathUtils.degToRad(itemData.rotation.x),
      MathUtils.degToRad(itemData.rotation.y),
      MathUtils.degToRad(itemData.rotation.z));

    //scale
    mesh.scale.set(itemData.scale.x, itemData.scale.y, itemData.scale.z);


    if (parentMesh) {
      parentMesh.add(mesh);
    } else {
      this.mgThree.scene.add(mesh);
    }

    mesh.userData['ItemData'] = itemData;
    itemData.mesh = mesh

    forEach(itemData.items, (itemData: ItemData) => {
      this.createItem(itemData, mesh);
    });

    // ClickActions
    this.handleItemClickActions(itemData);

    // visibility
    this.handleItemVisibility(itemData);

    this.allItems[itemData.id] = itemData;
  }

  handleItemClickActions(itemData: ItemData) {
    //console.log("handleItemClickActions",itemData);
    let action = null;
    if (this.playerData) {
      action = itemData.clickActions[this.playerData.id] || itemData.clickActions[''];
    }
    if (action) {
      this.addClickAction(itemData, action);
    } else {
      this.removeAction(itemData);
    }

  }

  handleItemVisibility(itemData: ItemData) {
    //console.log("handleItemVisibility",itemData);
    let isVisible: boolean = keys(itemData.visible).length == 0;
    if (this.playerData) {
      isVisible = isVisible || itemData.visible[this.playerData.id] == true;
    }
    //console.log("handleItemVisibility","isVisible",isVisible);
    itemData.mesh!.visible = isVisible;

    if (!isVisible) {
      this.removeAction(itemData);
    }
  }




  MeshClickFunc(event: any) {
    // console.log(event.point);
    // const direction = new THREE.Vector3();
    // direction.subVectors( event.target.position, event.point ) ;
    // console.log(direction);

    if (this.playerData) {

      let action = event.target.userData.ItemData.clickActions[this.playerData.id] || event.target.userData.ItemData.clickActions[''];
      SignalrService.singletone.executeAction(
        this.gameData.id,
        this.playerData.id,
        event.target.userData.ItemData.id,
        action,
        '',
        event.point);
    }
  }

  MeshMouseOverFunc(event: any) {
    // event.target.userData['c'] = event.target.material.clone().color;
    // event.target.material.color.set(0xff0000);
    document.body.style.cursor = 'pointer';
    this.mgThree.orbitControls.enabled = false;
  }

  MeshMouseOutFunc(event: any) {
    // let c: any = event.target.userData['c'];
    // event.target.material.color.set(c.r, c.g, c.b);
    document.body.style.cursor = 'default';
    this.mgThree.orbitControls.enabled = true;
  }

  onMeshClickFunc = this.MeshClickFunc.bind(this);
  onMeshMouseOverFunc = this.MeshMouseOverFunc.bind(this);
  onMeshMouseOutFunc = this.MeshMouseOutFunc.bind(this);

  addClickAction(itemData: ItemData, action: string) {
    //console.log("addClickAction", itemData ,action);

    this.removeAction(itemData);

    itemData.mesh!.addEventListener('click', this.onMeshClickFunc);

    itemData.mesh!.addEventListener('mouseover', this.onMeshMouseOverFunc);

    itemData.mesh!.addEventListener('mouseout', this.onMeshMouseOutFunc);

    this.mgThree.interactionManager.add(itemData.mesh!);
  }

  removeAction(itemData: ItemData) {
    //console.log("removeAction",itemData);
    itemData.mesh!.removeEventListener('click', this.onMeshClickFunc);
    itemData.mesh!.removeEventListener('click', this.onMeshMouseOverFunc);
    this.mgThree.interactionManager.remove(itemData.mesh!);
  }
}
